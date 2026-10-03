using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Services;

public partial class CheckoutExpirySweep
{
    /// <summary>
    /// Cancels the order behind a session that expired unpaid — the one destructive act in S7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only from <see cref="OrderStatus.Pending"/>, tested directly rather than through the
    /// transition table.</b> The table permits <c>Confirmed → Cancelled</c> and several more, which
    /// is right for a human with a reason and wrong for a timer: an order that reached Confirmed is
    /// on the pass and may already be cooked.
    /// </para>
    /// <para>
    /// <b>Only with no captured money</b>, because the likeliest ending for an abandoned Checkout is
    /// the diner paying cash at the till instead — and <b>both</b> that test and the status test are
    /// inside the conditional UPDATE rather than read into memory first. Every other write in this
    /// feature claims its row that way for the same reason: a cashier taking cash between a read and
    /// a save is otherwise silently overwritten, and on two app instances both would cancel and both
    /// would append a history row.
    /// </para>
    /// <para>
    /// <b>Only with no other live session</b>, since a second session means a payment may be in
    /// progress right now. This predicate is part of the serializable conditional UPDATE below,
    /// not a preflight read: a session created during the claim aborts the transaction instead of
    /// being missed by a time-of-check/time-of-use race.
    /// </para>
    /// </remarks>
    private async Task<bool> CancelAbandonedOrderAsync(
        Guid expiredSessionId, Guid orderId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        // A visit waiter must read allocations committed while waiting for its row lock.
        // Standalone orders retain the serializable no-live-checkout predicate. Visit orders
        // serialize with reservations and tender writes under the session-first order lock.
        var visitId = await _context.Orders.AsNoTracking().Where(value => value.Id == orderId)
            .Select(value => value.ServiceSessionId).SingleOrDefaultAsync(cancellationToken);
        await using var transaction = await _context.Database.BeginTransactionAsync(
            visitId.HasValue ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
        await using var accountMutation = await OrderAccountMutationScope.BeginAsync(
            _context, orderId, cancellationToken);
        var lockedVisitId = await _context.Orders.AsNoTracking().Where(value => value.Id == orderId)
            .Select(value => value.ServiceSessionId).SingleOrDefaultAsync(cancellationToken);
        if (visitId != lockedVisitId
            || await AccountPaymentLedgerGuard.HasProtectedOrderScopeAsync(_context, orderId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var claimed = await _context.Orders
            .Where(o => o.Id == orderId
                && o.Status == OrderStatus.Pending
                && !o.Payments.Any(p => CapturedStatuses.Contains(p.Status))
                && !_context.OrderCheckoutSessions.Any(session =>
                    session.OrderId == orderId
                    && session.Id != expiredSessionId
                    && session.Status == CheckoutSessionStatus.Created))
            .ExecuteUpdateAsync(
                o => o
                    .SetProperty(x => x.Status, OrderStatus.Cancelled)
                    .SetProperty(x => x.CancellationReason, CancellationReason)
                    .SetProperty(x => x.Version, x => x.Version + 1)
                    // ExecuteUpdate bypasses the IAuditable stamper, as everywhere else here.
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.UpdatedBy, auditId),
                cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogInformation(
                "Order {OrderId} was not cancellable when its checkout session expired — it is no longer "
                + "Pending, or it has been paid by another tender", orderId);
            return false;
        }

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Cancelled,
            Notes = "Online payment was not completed before the checkout session expired",
            ChangedAt = now,
            ChangedBy = auditId,
            CreatedAt = now,
            CreatedBy = auditId,
        });

        accountMutation.RecordAccountChange();
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await AnnounceAsync(orderId, cancellationToken);

        _logger.LogWarning("Cancelled order {OrderId}: its online payment was never completed", orderId);

        return true;
    }

}
