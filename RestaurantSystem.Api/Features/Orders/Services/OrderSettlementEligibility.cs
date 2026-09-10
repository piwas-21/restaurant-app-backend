using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Decides whether the staff till may collect another tender for an order.
/// </summary>
/// <remarks>
/// Fulfilment and settlement are independent: a completed service can still be unpaid.
/// Cancellation, refund and void states are terminal for collection. A void has no distinct
/// persisted enum in this model; it is represented by a cancelled order or a refunded tender.
/// </remarks>
public static class OrderSettlementEligibility
{
    private const decimal PaymentTolerance = 0.01m;

    /// <summary>
    /// True only when the order has an outstanding balance and no terminal reversal state.
    /// </summary>
    public static bool CanCollect(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            return false;
        }

        if (order.PaymentStatus == PaymentStatus.Refunded || HasRefundedTender(order))
        {
            return false;
        }

        // Do not use the fulfilment status here. Completed means service finished, not that
        // the diner paid. TotalPaid also makes an overpaid order ineligible even if a stale
        // PaymentStatus has not yet been recomputed.
        return order.TotalPaid < order.Total - PaymentTolerance;
    }

    private static bool HasRefundedTender(Order order) => order.Payments.Any(payment =>
        payment.Status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
        || payment.IsRefunded
        || payment.RefundedAmount > 0);
}
