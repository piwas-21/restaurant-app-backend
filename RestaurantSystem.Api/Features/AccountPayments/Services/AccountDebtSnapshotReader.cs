using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal interface IAccountDebtSnapshotReader
{
    Task<AccountPaymentAccountSnapshot> ReadAsync(Guid serviceSessionId, CancellationToken cancellationToken);
}

internal sealed record AccountPaymentAccountSnapshot(
    TableServiceSession Session, AccountMoney Money, AccountDebtSnapshot Debt);

/// <summary>Reads one consistent visit ledger; expiry alone never releases provider-held money.</summary>
internal sealed class AccountDebtSnapshotReader(ApplicationDbContext context) : IAccountDebtSnapshotReader
{
    public Task<AccountPaymentAccountSnapshot> ReadAsync(
        Guid serviceSessionId, CancellationToken cancellationToken) =>
        ReadCoreAsync(serviceSessionId, null, cancellationToken);

    /// <summary>The capture writer may ignore only its own already verified journal while posting its tender.</summary>
    internal Task<AccountPaymentAccountSnapshot> ReadForVerifiedCaptureAsync(
        Guid serviceSessionId, Guid attemptId, CancellationToken cancellationToken) =>
        ReadCoreAsync(serviceSessionId, attemptId, cancellationToken);

    private async Task<AccountPaymentAccountSnapshot> ReadCoreAsync(
        Guid serviceSessionId, Guid? verifiedAttemptId, CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var session = await context.TableServiceSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken)
            ?? throw new NotFoundException("Table account was not found.");
        await AccountCheckoutLedgerGuard.RequireReconciledAsync(context, serviceSessionId, cancellationToken, verifiedAttemptId);
        var money = new AccountMoney(session.Currency);
        var orders = await context.Orders.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId && !value.IsDeleted)
            .Include(value => value.Items).Include(value => value.Payments).Include(value => value.ExternalReference)
            .AsSplitQuery().ToListAsync(cancellationToken);
        var orderIds = orders.Select(order => order.Id).ToHashSet();
        var attempts = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId)
            .Include(value => value.Allocations).ThenInclude(value => value.OrderPayment)
            .AsSplitQuery().ToListAsync(cancellationToken);
        var checkouts = await context.OrderCheckoutSessions.AsNoTracking()
            .Where(value => orderIds.Contains(value.OrderId))
            .ToListAsync(cancellationToken);
        var amendments = await context.Set<OrderAmendment>().AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId
                && value.State == OrderAmendmentState.Committed)
            .ToListAsync(cancellationToken);
        if (amendments.Any(amendment => !orderIds.Contains(amendment.SourceOrderId)))
            throw new ConflictException("A committed amendment references an unavailable source order.");
        foreach (var amendment in amendments)
            OrderAmendmentFinancialGuard.AssertResolved(amendment.FinancialResolutionJson);
        var amendmentRefunds = await AccountAmendmentRefundIntegrity.ReadAsync(
            context, orders, amendments, attempts, money, cancellationToken);
        await OrderBillingCreditConsistency.AssertAsync(context, orderIds, cancellationToken);
        ValidateCurrency(orders, money.Currency);
        AccountCheckoutEvidenceGuard.Validate(orders, checkouts, attempts, money);
        AccountPaymentAttemptScopeGuard.RequireReconciled(attempts, money);
        var capturedAllocations = attempts.Where(value => value.State == AccountPaymentState.Captured)
            .SelectMany(value => value.Allocations).ToArray();
        var captured = AccountPaymentAllocationReversalMath.Apply(
            capturedAllocations, amendmentRefunds.Reversals, money);
        var reserved = attempts.Where(value => value.State.HoldsReservation())
            .SelectMany(value => value.Allocations).Select(Segment).ToArray();
        var debt = AccountDebtProjection.Project(orders, money, captured, reserved, amendments,
            session.BillingAllocationVersion, amendmentRefunds.AuthorizedRefundMinorByPayment);
        return new(session, money, debt);
    }

    private static void ValidateCurrency(IReadOnlyList<Order> orders, string currency)
    {
        if (orders.Any(order => order.Type != OrderType.DineIn
                || order.Payments.Any(payment => payment.Currency is not null
                    && !string.Equals(payment.Currency.Trim(), currency, StringComparison.OrdinalIgnoreCase))))
            throw new ConflictException("The table account contains incompatible order or payment currencies.");
    }

    private static AccountDebtSegment Segment(AccountPaymentAllocation value) =>
        new(value.OrderId, value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit);
}
