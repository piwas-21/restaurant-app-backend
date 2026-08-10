using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <summary>
/// Turns what Stripe reported into the order's <c>OrderPayment</c> row.
///
/// <para>
/// Its own type rather than a method on the settlement writer because it is the single place a
/// Stripe tender is written, and the one place the amount charged crosses from Stripe's minor units
/// into the ledger's decimal. Everything around it — the claim, the confirm, the notifications — is
/// orchestration; this is the money.
/// </para>
/// </summary>
public static class OnlineTenderCompletion
{
    /// <summary>Written to <c>OrderPayment.PaymentGateway</c>, the column that already names one.</summary>
    private const string GatewayName = "Stripe";

    /// <summary>
    /// Every currency Sofra can onboard a tenant in is two-decimal, which is what lets this be a
    /// constant rather than a lookup. <c>CheckoutAmount</c> enforces that on the way out; this is
    /// the same conversion on the way back in.
    /// </summary>
    private const int MinorUnitsPerMajor = 100;

    /// <summary>
    /// Completes the tender order creation minted, or mints one if there is none.
    /// </summary>
    /// <remarks>
    /// Reusing the <c>Processing</c> tender is the normal path — it is the record that has been
    /// telling every other surface "money is in flight" since the order was placed. Creating one
    /// when it is absent keeps settlement independent of how the order was placed: a staff-created
    /// order, or a future caller that skipped the declared tender, still gets an accurate ledger
    /// rather than a silently unrecorded payment.
    /// </remarks>
    /// <param name="amountReceivedMinor">
    /// What STRIPE says it took, not what the order said it wanted. The caller asserts the two are
    /// equal before this runs, so they agree today — but if that assertion is ever relaxed, the
    /// ledger must record the money that actually moved.
    /// </param>
    public static OrderPayment Apply(
        Order order,
        OrderCheckoutSession session,
        string? paymentIntentId,
        long? amountReceivedMinor,
        string auditId,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(session);

        var tender = order.Payments.FirstOrDefault(
            p => p.PaymentMethod == PaymentMethod.OnlinePayment && p.Status == PaymentStatus.Processing);

        if (tender is null)
        {
            tender = new OrderPayment
            {
                OrderId = order.Id,
                PaymentMethod = PaymentMethod.OnlinePayment,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = auditId,
            };

            order.Payments.Add(tender);
        }
        else
        {
            tender.UpdatedAt = now;
            tender.UpdatedBy = auditId;
        }

        tender.Amount = (amountReceivedMinor ?? session.AmountMinor) / (decimal)MinorUnitsPerMajor;
        tender.Currency = session.Currency;
        tender.TransactionId = paymentIntentId;
        tender.PaymentGateway = GatewayName;
        tender.Status = PaymentStatus.Completed;

        return tender;
    }
}
