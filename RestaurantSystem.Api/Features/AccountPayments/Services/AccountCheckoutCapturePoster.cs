using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Posts the original frozen scope once; provider evidence is already durably committed.</summary>
public sealed class AccountCheckoutCapturePoster(ApplicationDbContext context,
    IAccountPaymentCaptureWriter writer, IOrderFidelityCoordinator fidelity,
    IOrderMappingService mapping, IOrderEventService events, TimeProvider clock,
    ILogger<AccountCheckoutCapturePoster> logger) : IAccountCheckoutCapturePoster
{
    public async Task PostAsync(AccountCheckoutJournal verified, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Provider posting must own its money transaction.");
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        var locked = await AccountCheckoutLocks.LoadAsync(context, verified.AttemptId, cancellationToken);
        RequireCurrentEvidence(locked.Journal, verified);
        var attempt = locked.Attempt;
        if (attempt.State == AccountPaymentState.Captured)
        {
            await new AccountDebtSnapshotReader(context).ReadAsync(attempt.ServiceSessionId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        await writer.RecordVerifiedProviderAsync(attempt, locked.Journal, cancellationToken);
        locked.Journal.ReconciliationRequired = false;
        locked.Session.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await ResolveHandoffAsync(attempt, cancellationToken);
        var orderIds = attempt.Allocations.Select(value => value.OrderId).Distinct().ToArray();
        var orders = await context.Orders.Where(value => orderIds.Contains(value.Id)).ToListAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await NotifyAfterCommitAsync(orders, attempt.Id, cancellationToken);
    }

    private void RequireCurrentEvidence(AccountCheckoutJournal current, AccountCheckoutJournal verified)
    {
        if (verified.LeaseId is null || current.LeaseId != verified.LeaseId
            || current.LeaseExpiresAt is not DateTime leaseExpires || leaseExpires <= clock.GetUtcNow().UtcDateTime
            || current.CreatePayloadHash != verified.CreatePayloadHash
            || current.LastVerifiedAt is null || current.LastVerifiedAt != verified.LastVerifiedAt
            || current.ProviderCapturedMinor != verified.ProviderCapturedMinor
            || current.ProviderRefundedMinor != 0 || current.ProviderChargeId != verified.ProviderChargeId)
            throw new ConflictException("Canonical payment evidence changed before allocation posting.");
    }

    private async Task ResolveHandoffAsync(AccountPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var handoff = await context.TableServicePaymentHandoffs.SingleOrDefaultAsync(value =>
            value.ServiceSessionId == attempt.ServiceSessionId && value.Status == TableServicePaymentHandoffStatus.Requested,
            cancellationToken);
        if (handoff is null) return;
        var account = await new AccountDebtSnapshotReader(context).ReadAsync(attempt.ServiceSessionId, cancellationToken);
        if (account.Debt.OutstandingMinor != 0 || account.Debt.ReservedMinor != 0) return;
        handoff.Status = TableServicePaymentHandoffStatus.Resolved;
        handoff.ResolvedAt = clock.GetUtcNow().UtcDateTime;
        handoff.ResolvedBy = attempt.CreatedBy;
        handoff.ResolvedAccountPaymentAttemptId = attempt.Id;
    }

    private async Task NotifyAfterCommitAsync(IEnumerable<Order> orders, Guid attemptId,
        CancellationToken cancellationToken)
    {
        foreach (var order in orders)
        {
            try
            {
                if (order.PaymentStatus == PaymentStatus.Completed)
                    await fidelity.AwardEarnedPointsAsync(order, order.UserId, cancellationToken);
                var dto = await mapping.MapToOrderDtoAsync(order, cancellationToken);
                await events.NotifyFocusOrderUpdate(dto);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Post-commit account notification deferred for {AttemptId}: {FailureType}",
                    attemptId, exception.GetType().Name);
            }
        }
    }
}
