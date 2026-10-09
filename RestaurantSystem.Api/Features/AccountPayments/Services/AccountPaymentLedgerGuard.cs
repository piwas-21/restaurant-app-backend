using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Preserves reviewed unit scopes when an older client tries to write money.</summary>
internal static class AccountPaymentLedgerGuard
{
    internal static async Task RequireLegacyCollectionAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var states = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId)
            .Select(value => value.State).ToListAsync(cancellationToken);
        if (states.Any(value => value == AccountPaymentState.Captured || value.HoldsReservation()))
            throw new ConflictException("Use the account payment flow to preserve paid and reserved item scopes.");
    }

    internal static async Task<TableServiceSession?> LockOrderAccountAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var membership = await context.Orders.AsNoTracking()
            .Where(value => value.Id == orderId).Select(value => value.ServiceSessionId)
            .SingleOrDefaultAsync(cancellationToken);
        var session = membership.HasValue
            ? await TableServiceSessionRowLock.LoadAsync(context, membership.Value, cancellationToken)
                ?? throw new ConflictException("The order's table account is missing.")
            : null;
        var current = await context.Orders.AsNoTracking()
            .Where(value => value.Id == orderId).Select(value => value.ServiceSessionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (membership != current)
            throw new ConflictException("The order moved to a table account. Refresh before changing its payment.");
        return session;
    }

    internal static async Task RequireNoPendingAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var states = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId)
            .Select(value => value.State).ToListAsync(cancellationToken);
        if (states.Any(value => value.HoldsReservation()))
            throw new ConflictException("Resolve reserved or processing account payments before closing the visit.");
    }

    internal static async Task RequireOrderCorrectionAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        if (await HasProtectedOrderScopeAsync(context, orderId, cancellationToken))
            throw new ConflictException("Resolve the order's reserved or allocated account payment before cancelling its items.");
    }

    internal static async Task RequireOrderCancellationAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        await OrderAmendmentFinancialGuard.AssertNoPendingSourceResolutionAsync(
            context, orderId, cancellationToken);

        var order = await context.Orders.AsNoTracking().Include(value => value.Payments)
            .SingleOrDefaultAsync(value => value.Id == orderId && !value.IsDeleted, cancellationToken)
            ?? throw new ConflictException("The order changed. Refresh before cancelling.");
        if (order.ServiceSessionId is not Guid serviceSessionId)
        {
            await RequireOrderCorrectionAsync(context, orderId, cancellationToken);
            return;
        }

        var session = await context.TableServiceSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken)
            ?? throw ReconciliationRequired();
        var attempts = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.Allocations.Any(allocation => allocation.OrderId == orderId))
            .Include(value => value.Allocations).ThenInclude(value => value.OrderPayment)
            .Include(value => value.CashCollectionReceipt)
            .AsSplitQuery().ToListAsync(cancellationToken);
        if (attempts.Any(value => value.State.HoldsReservation()))
            throw ProtectedOrderScope();

        if (attempts.Any(value => value.ServiceSessionId != serviceSessionId))
            throw ReconciliationRequired();
        var capturedAttempts = attempts.Where(value => value.State == AccountPaymentState.Captured).ToArray();
        if (capturedAttempts.Length == 0)
            return;

        var money = new AccountMoney(session.Currency);
        AccountPaymentAttemptScopeGuard.RequireReconciled(capturedAttempts, money);
        var allCapturedAllocations = capturedAttempts.SelectMany(value => value.Allocations).ToArray();
        var allocationOrderIds = allCapturedAllocations.Select(value => value.OrderId).Distinct().ToArray();
        var sessionOrderIds = await context.Orders.AsNoTracking()
            .Where(value => allocationOrderIds.Contains(value.Id) && !value.IsDeleted
                && value.ServiceSessionId == serviceSessionId)
            .Select(value => value.Id).ToArrayAsync(cancellationToken);
        if (sessionOrderIds.Length != allocationOrderIds.Length)
            throw ReconciliationRequired();

        if (allCapturedAllocations.Any(value => value.OrderPayment?.Currency is not string currency
                || string.IsNullOrWhiteSpace(currency)
                || !string.Equals(currency.Trim(), money.Currency, StringComparison.OrdinalIgnoreCase)))
            throw ReconciliationRequired();

        await AccountCapturedPaymentCancellationEvidence.ValidateAsync(
            context, capturedAttempts, money, cancellationToken);

        var capturedAllocations = allCapturedAllocations.Where(value => value.OrderId == orderId).ToArray();
        var orderPaymentIds = order.Payments.Select(value => value.Id).ToHashSet();
        if (capturedAllocations.Any(value => value.OrderPaymentId is null
                || !orderPaymentIds.Contains(value.OrderPaymentId.Value)))
            throw ProtectedOrderScope();

        ValidateCapturedPaymentSnapshots(order, capturedAllocations, money);

        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == orderId && value.State == OrderAmendmentState.Committed)
            .ToListAsync(cancellationToken);
        if (amendments.Any(value => value.ServiceSessionId != serviceSessionId))
            throw ReconciliationRequired();

        var capturedAttemptIds = capturedAttempts.Select(value => value.Id).ToArray();
        var targetAttempts = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => capturedAttemptIds.Contains(value.Id))
            .Include(value => value.Allocations.Where(allocation => allocation.OrderId == orderId))
                .ThenInclude(value => value.OrderPayment)
            .Include(value => value.CashCollectionReceipt)
            .AsSplitQuery().ToListAsync(cancellationToken);
        var refundHistory = await AccountAmendmentRefundIntegrity.ReadAsync(
            context, [order], amendments, targetAttempts, money, cancellationToken,
            cancellationCashTargetOrderId: orderId);
        var remaining = AccountPaymentAllocationReversalMath.Apply(
            capturedAllocations, refundHistory.Reversals, money);
        if (remaining.Count > 0)
            throw ProtectedOrderScope();
    }

    internal static async Task<bool> HasProtectedOrderScopeAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var states = await context.AccountPaymentAllocations.AsNoTracking()
            .Where(value => value.OrderId == orderId).Select(value => value.Attempt!.State)
            .ToListAsync(cancellationToken);
        return states.Any(value => value == AccountPaymentState.Captured || value.HoldsReservation());
    }

    private static void RequireTransaction(ApplicationDbContext context)
    {
        if (context.Database.CurrentTransaction is null)
            throw new ConflictException("Account payment writes require a locked transaction.");
    }

    private static void ValidateCapturedPaymentSnapshots(
        Order order, IReadOnlyList<AccountPaymentAllocation> allocations, AccountMoney money)
    {
        var payments = order.Payments.ToDictionary(value => value.Id);
        if (allocations.Any(value => value.OrderId != order.Id || value.OrderPaymentId is null
                || !payments.ContainsKey(value.OrderPaymentId.Value)))
            throw ReconciliationRequired();

        foreach (var group in allocations.GroupBy(value => value.OrderPaymentId!.Value))
        {
            var payment = payments[group.Key];
            if (payment.OrderId != order.Id || !payment.Status.IsCaptured()
                || string.IsNullOrWhiteSpace(payment.Currency)
                || !string.Equals(payment.Currency.Trim(), money.Currency, StringComparison.OrdinalIgnoreCase)
                || group.Sum(value => value.AmountMinor) != money.ToMinor(payment.Amount))
                throw ReconciliationRequired();
        }
    }

    private static ConflictException ProtectedOrderScope() => new(
        "Resolve the order's reserved or allocated account payment before cancelling its items.");

    private static ConflictException ReconciliationRequired() => new(
        "The order's captured account allocations require refund reconciliation before cancellation.");
}
