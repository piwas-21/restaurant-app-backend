using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountPaymentCaptureService(
    ApplicationDbContext context, IAccountPaymentActorResolver actors, IAccountPaymentCaptureWriter writer,
    IOrderFidelityCoordinator fidelity, TimeProvider timeProvider,
    ILogger<AccountPaymentCaptureService> logger) : IAccountPaymentCaptureService
{
    public async Task<AccountPaymentOperationDto> CaptureManualAsync(Guid sessionId, Guid operationId,
        CaptureAccountPaymentRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(sessionId, operationId, request.ExpectedVersion);
        var actor = actors.ResolveStaffActor();
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Manual collection must own its money transaction.");
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var attempt = await LoadOwnedAttemptAsync(sessionId, operationId, actor, cancellationToken);
            var snapshot = AccountPaymentSnapshots.Deserialize<AccountPaymentQuoteSnapshot>(attempt.SnapshotJson);
            if (attempt.State == AccountPaymentState.Captured)
            {
                AccountCashCaptureReceiptPolicy.RequireReplay(attempt, snapshot, actor, request);
                await transaction.CommitAsync(cancellationToken);
                return AccountPaymentSnapshots.ToOperation(attempt);
            }
            ValidateReservedAttempt(session.Status, attempt, request.ExpectedVersion);

            var receipt = AccountCashCaptureReceiptPolicy.Create(attempt, snapshot, actor,
                request, actor.AuditIdentifier, timeProvider.GetUtcNow().UtcDateTime);

            // Later rounds do not alter this reviewed scope; the writer revalidates its exact debt.
            await writer.RecordManualAsync(attempt, cancellationToken);
            if (receipt is not null)
            {
                var capturedAt = attempt.CompletedAt
                    ?? throw new ConflictException("The cash collection timestamp requires reconciliation.");
                receipt.CapturedAt = capturedAt;
                receipt.CreatedAt = capturedAt;
                attempt.CashCollectionReceipt = receipt;
                context.Set<AccountCashCollectionReceipt>().Add(receipt);
            }
            session.RecordAccountChange();
            await context.SaveChangesAsync(cancellationToken);
            var paidOrders = await LoadPaidOrdersAsync(attempt, cancellationToken);
            await ResolveHandoffAsync(sessionId, attempt.Id, actor.AuditIdentifier, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await AwardPaidOrdersAsync(paidOrders, operationId, cancellationToken);
            return AccountPaymentSnapshots.ToOperation(attempt);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("The account changed while collection was being recorded.", exception);
        }
    }

    private static void ValidateRequest(Guid sessionId, Guid operationId, int expectedVersion)
    {
        if (sessionId == Guid.Empty || operationId == Guid.Empty || expectedVersion <= 0)
            throw new BadRequestException("A table visit, payment operation and positive version are required.");
    }

    private async Task<AccountPaymentAttempt> LoadOwnedAttemptAsync(
        Guid sessionId, Guid operationId, AccountPaymentActor actor, CancellationToken cancellationToken)
    {
        var attempt = await context.AccountPaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM account_payment_attempts WHERE operation_id = {operationId} FOR UPDATE")
            .Include(value => value.Allocations)
            .Include(value => value.CashCollectionReceipt)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The payment operation was not found for this table visit.");
        if (attempt.ServiceSessionId != sessionId || attempt.ActorId != actor.ActorId || attempt.ActorKind != actor.Kind)
            throw new NotFoundException("The payment operation was not found for this table visit.");
        if (attempt.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new ConflictException("Online contributions require verified provider settlement.");
        return attempt;
    }

    private void ValidateReservedAttempt(
        TableServiceSessionStatus sessionStatus, AccountPaymentAttempt attempt, int expectedVersion)
    {
        if (sessionStatus != TableServiceSessionStatus.Open || attempt.State != AccountPaymentState.Reserved
            || attempt.Version != expectedVersion)
            throw new ConflictException("The contribution changed. Review it before recording collection.");
        if (attempt.ReservationExpiresAt is not DateTime expires || expires <= timeProvider.GetUtcNow().UtcDateTime)
            throw new ConflictException("The collection reservation expired. Release it and review a new contribution.");
    }

    private async Task<List<Order>> LoadPaidOrdersAsync(
        AccountPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var orderIds = attempt.Allocations.Select(value => value.OrderId).Distinct().ToArray();
        return await context.Orders.Where(value => orderIds.Contains(value.Id)
            && value.PaymentStatus == PaymentStatus.Completed).ToListAsync(cancellationToken);
    }

    private async Task AwardPaidOrdersAsync(
        IEnumerable<Order> orders, Guid operationId, CancellationToken cancellationToken)
    {
        foreach (var order in orders)
        {
            try { await fidelity.AwardEarnedPointsAsync(order, order.UserId, cancellationToken); }
            catch (Exception exception)
            {
                logger.LogError(exception, "Loyalty award failed after committed account payment {OperationId}", operationId);
            }
        }
    }

    private async Task ResolveHandoffAsync(Guid sessionId, Guid attemptId, string audit, CancellationToken cancellationToken)
    {
        var handoff = await context.TableServicePaymentHandoffs.SingleOrDefaultAsync(value =>
            value.ServiceSessionId == sessionId && value.Status == TableServicePaymentHandoffStatus.Requested,
            cancellationToken);
        if (handoff is null) return;
        var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
        if (account.Debt.OutstandingMinor != 0 || account.Debt.ReservedMinor != 0) return;
        handoff.Status = TableServicePaymentHandoffStatus.Resolved;
        handoff.ResolvedAt = timeProvider.GetUtcNow().UtcDateTime;
        handoff.ResolvedBy = audit;
        handoff.ResolvedAccountPaymentAttemptId = attemptId;
    }
}
