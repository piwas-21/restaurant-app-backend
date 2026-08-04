namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// The order lifecycle, and the single source of truth for it. The frontend
/// mirrors this table entry for entry in <c>lib/orderStatus.ts</c>; a rule added
/// here without one added there puts a status on the cashier's dropdown that the
/// server then refuses.
/// </summary>
public static class OrderStatusTransitions
{
    /// <summary>
    /// Whether an order may move from <paramref name="currentStatus"/> to
    /// <paramref name="newStatus"/>.
    ///
    /// <para>
    /// <b><see cref="OrderStatus.Delivered"/> and <see cref="OrderStatus.Refunded"/>
    /// are not part of the lifecycle.</b> They are enum members no code path
    /// writes and no arm below targets, so an order cannot arrive at either —
    /// verified against production (2026-08-02: every order row and every
    /// status-history entry is <c>Pending</c> or <c>Confirmed</c>). They are
    /// listed explicitly rather than left to the discard arm so that "refused"
    /// reads as a decision. Issue #287 was raised because it did not:
    /// <see cref="OrderStatus.Completed"/> and <see cref="OrderStatus.Cancelled"/>
    /// are refused WITH a comment, and a delivered order silently falling
    /// through beside them looks like an oversight — one you would fix by adding
    /// an exit, when the missing piece is an entrance.
    /// </para>
    ///
    /// <para>
    /// If delivery ever needs its own step (a courier marks delivered, the till
    /// settles later), it needs BOTH a rule INTO <c>Delivered</c> — from
    /// <c>OutForDelivery</c> — and a rule out of it. Adding only the exit changes
    /// nothing. Refunds are money, not lifecycle: they live on
    /// <c>OrderPayment.Status</c>, which <c>RefundPaymentCommand</c> owns.
    /// </para>
    /// </summary>
    public static bool IsValid(OrderStatus currentStatus, OrderStatus newStatus)
    {
        return currentStatus switch
        {
            OrderStatus.Pending => newStatus is OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.PendingApproval,
            OrderStatus.PendingApproval => newStatus is OrderStatus.Confirmed or OrderStatus.Cancelled,
            OrderStatus.Confirmed => newStatus is OrderStatus.Preparing or OrderStatus.Cancelled,
            OrderStatus.Preparing => newStatus is OrderStatus.Ready or OrderStatus.Cancelled,
            OrderStatus.Ready => newStatus is OrderStatus.OutForDelivery or OrderStatus.Completed or OrderStatus.Cancelled,
            OrderStatus.OutForDelivery => newStatus is OrderStatus.Completed or OrderStatus.Cancelled,
            OrderStatus.Completed => false, // Cannot change from completed
            OrderStatus.Cancelled => false, // Cannot change from cancelled
            OrderStatus.Delivered => false, // Unreachable: nothing transitions INTO Delivered — see the note above
            OrderStatus.Refunded => false,  // Unreachable: refunds are a PAYMENT state, not an order one
            _ => false
        };
    }
}
