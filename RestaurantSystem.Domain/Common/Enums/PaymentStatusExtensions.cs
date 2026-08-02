namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// Shared reads over <see cref="PaymentStatus"/> as it appears on an
/// <c>OrderPayment</c>. The same enum is also used for an <c>Order</c>'s
/// aggregate payment state, where a different subset of members is meaningful
/// — <c>PartiallyPaid</c> and <c>Overpaid</c> describe an order's balance and
/// are never written to a single tender.
/// </summary>
public static class PaymentStatusExtensions
{
    /// <summary>
    /// True when the restaurant actually took this tender, regardless of what
    /// was later given back. Money reports and the order's <c>TotalPaid</c>
    /// both need this set: a refund does not un-charge the original payment,
    /// it books a separate outflow, so the gross amount stays counted and the
    /// refunded amount is subtracted alongside it.
    /// </summary>
    /// <remarks>
    /// Excluding <c>Refunded</c>/<c>PartiallyRefunded</c> here would double-count
    /// the refund: the payment would drop out of the gross sum *and* be
    /// subtracted as a refund, driving <c>TotalPaid</c> negative.
    /// </remarks>
    public static bool IsCaptured(this PaymentStatus status) =>
        status is PaymentStatus.Completed
               or PaymentStatus.PartiallyRefunded
               or PaymentStatus.Refunded;
}
