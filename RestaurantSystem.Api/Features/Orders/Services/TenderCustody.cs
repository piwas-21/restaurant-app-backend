using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Answers the one question the refund paths have never asked: <b>who is holding this money?</b>
/// </summary>
/// <remarks>
/// <para>
/// A tender the restaurant took at its own till is cash in a drawer or a capture on its own
/// terminal, and giving it back is a physical act this system merely records. A tender captured
/// through a payment <i>gateway</i> is money sitting at that gateway, and this system cannot move
/// it: the platform's Stripe key deliberately carries <b>no refunds write</b> (SOFRA-PAYMENTS-PLAN
/// §4 — it is what makes a leaked key survivable). Writing <c>RefundedAmount</c> for one of those
/// is not a refund, it is a ledger claiming a refund that never happened, and the diner is still
/// out the money while every till and Z-report reports it returned.
/// </para>
/// <para>
/// The test is the presence of a gateway name, not the string <c>"Stripe"</c>. The column already
/// means "an external processor captured this" (<c>OrderPayment.PaymentGateway</c> — "Stripe,
/// PayPal, etc."), so keying off it refuses the next gateway as well as this one, and the refusal
/// can name whichever one the row records rather than guessing. What makes the rule bite is that
/// <c>OnlineTenderCompletion</c> stamps the name on <b>every</b> Stripe capture — pinned by test,
/// because deleting that one assignment would silently turn this whole guard off.
/// </para>
/// <para>
/// It fires on nothing that exists today outside the Stripe path: no customer or staff surface in
/// the frontend sends <c>paymentGateway</c> on the till endpoint (three declarations, zero writers,
/// measured across the repo), so every tender RUMI has ever taken reads as till-held.
/// </para>
/// <para>
/// <b>Which is exactly why <c>AddPaymentToOrderCommand</c> no longer binds that field.</b> It used
/// to copy it verbatim out of the staff request body into a tender it then marks
/// <c>Completed</c> — so a caller could set it on a <b>cash</b> payment and lock that money out of
/// the refund path permanently, since the refusal here is deliberately not overridable. Zero
/// frontend writers is a property of today's UI, not of the server. A guard is only as fail-safe as
/// the field it reads: before keying one on a column, find every writer of the column, not every
/// writer the UI exercises. Removing it is the same fix #328 made to the same field on the
/// anonymous order path; the only writer left is the Stripe settle path, the one thing that has
/// actually seen a gateway. An unknown JSON property is ignored on bind, so a stale client that
/// still sends it is unaffected.
/// </para>
/// </remarks>
public static class TenderCustody
{
    /// <summary>
    /// True when an external payment gateway holds the money, so this system can record a refund
    /// but cannot perform one.
    /// </summary>
    public static bool IsHeldByGateway(OrderPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return !string.IsNullOrWhiteSpace(payment.PaymentGateway);
    }

    /// <summary>
    /// The refusal shown to staff, naming the gateway that must issue the refund.
    /// </summary>
    /// <remarks>
    /// It says where to go, not merely no. The charge sits at Stripe on the restaurant's own
    /// connected account and only Stripe can return it — our key carries no refunds write — so
    /// naming the gateway is the actionable half of the refusal; a message that only refused would
    /// read as a broken button. (It used to say the restaurant "owns the account outright, Connect
    /// Standard": under Connect Express the platform is the account's controller, so that reason
    /// no longer holds even though the refusal does.)
    /// </remarks>
    public static string RefusalMessage(OrderPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return $"This payment was captured by {payment.PaymentGateway} and must be refunded from "
               + $"your {payment.PaymentGateway} dashboard. Recording it here would report money "
               + "returned that never left the gateway.";
    }
}
