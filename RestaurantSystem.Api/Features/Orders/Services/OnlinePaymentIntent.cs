using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// What an order looks like at CREATION when the diner intends to pay online.
///
/// <para>
/// Its own type, and here rather than beside <c>OnlinePaymentEligibility</c> in the Payments
/// feature, because the two answer opposite halves of one question — that one guards the money end
/// (may this order still be charged?), this one the ORDER end (what state does an order start in
/// when it is going to be charged?). Both are pure policy; neither touches Stripe or the database.
/// </para>
/// </summary>
public static class OnlinePaymentIntent
{
    /// <summary>
    /// Whether the caller declared an online tender when placing the order.
    /// </summary>
    /// <remarks>
    /// This is an INTENT, not a payment. <c>OrderPaymentBuilder</c> is what turns it into a
    /// <c>Processing</c> tender, and only the settle path may complete one.
    /// </remarks>
    public static bool IsDeclaredIn(IReadOnlyCollection<CreateOrderPaymentDto> payments)
    {
        ArgumentNullException.ThrowIfNull(payments);

        return payments.Any(p => p.PaymentMethod == PaymentMethod.OnlinePayment);
    }

    /// <summary>
    /// The status an order starts in.
    /// </summary>
    /// <remarks>
    /// Dine-in normally auto-confirms at creation, and <c>PrinterFeedQuery</c> puts a
    /// <c>Confirmed</c> order in front of the kitchen. An order that has not been paid for yet must
    /// not get that far, so an online tender holds ALL THREE order types at <c>Pending</c> until the
    /// settle path confirms — the one behavioural change online payment makes to order creation.
    ///
    /// <para>
    /// This has to be decided HERE rather than when the Stripe session is minted, and that is the
    /// whole reason the tender is created at order time. A dine-in order is <c>Confirmed</c> — and
    /// therefore printed — before <c>POST /api/payments/checkout-session</c> is ever called. There
    /// is no later point at which a ticket can be un-printed.
    /// </para>
    /// </remarks>
    public static OrderStatus InitialStatus(OrderType type, bool paysOnline)
    {
        if (paysOnline)
        {
            return OrderStatus.Pending;
        }

        return type == OrderType.DineIn ? OrderStatus.Confirmed : OrderStatus.Pending;
    }

    /// <summary>The status-history note explaining <see cref="InitialStatus"/>.</summary>
    public static string InitialStatusNote(OrderType type, bool paysOnline)
    {
        if (paysOnline)
        {
            return "Order created, awaiting online payment";
        }

        return type == OrderType.DineIn ? "Order created and auto-confirmed (Dine-in)" : "Order created";
    }

    /// <summary>
    /// Whether this order still has an online payment outstanding, and so must not be confirmed.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="InitialStatus"/>: that decides an order may not be confirmed at
    /// creation, this decides it may not be confirmed later either — until the money is in. Both
    /// exist because <c>PrinterFeedQuery</c> prints any <c>Confirmed</c> order.
    ///
    /// <para>
    /// UNPAID is half the test, and not belt-and-braces. The likeliest end for an online payment in
    /// a restaurant is the diner giving up and paying at the till, and <c>AddPaymentToOrder</c>
    /// sweeps only <c>Pending</c> tenders — so the abandoned <c>Processing</c> one outlives the cash
    /// that replaced it. Keyed on its mere existence, the order was trapped: <c>Pending</c> leads
    /// only to <c>Confirmed</c>, <c>Cancelled</c> or <c>PendingApproval</c>, and
    /// <c>PendingApproval</c> only back to the first two — so Confirm was refused forever, and
    /// Cancel was the sole remaining move on an order the restaurant had already been paid for.
    /// </para>
    /// <para>
    /// Scoped to <c>OnlinePayment</c> for the same reason it is worded that way: nothing else
    /// reaches <c>Processing</c> today, and a future tender that did would be blocked here for a
    /// reason nobody stated.
    /// </para>
    /// </remarks>
    public static bool IsAwaitingPayment(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.PaymentStatus is PaymentStatus.Completed or PaymentStatus.Overpaid)
        {
            return false;
        }

        return order.Payments.Any(p =>
            p.PaymentMethod == PaymentMethod.OnlinePayment && p.Status == PaymentStatus.Processing);
    }
}
