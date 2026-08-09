using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <summary>
/// Whether an order may still be paid online. Its own type rather than a private method on the
/// handler because it is pure order policy — no Stripe, no database — and S5's settle path and
/// S7's reconciler both need the same answer about the same order.
/// </summary>
public static class OnlinePaymentEligibility
{
    /// <summary>
    /// Throws unless the order can still take an online payment.
    /// </summary>
    /// <remarks>
    /// "Finished" is asked of <see cref="OrderStatusTransitions"/> rather than listed again here.
    /// An order that can no longer even be cancelled is over, and that table is the single source
    /// of truth for the lifecycle — a second list beside it is the kind of copy that drifts, and
    /// the copy that drifts here would be the one letting a closed order be charged.
    /// </remarks>
    public static void EnsurePayable(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (!OrderStatusTransitions.IsValid(order.Status, OrderStatus.Cancelled))
        {
            throw new BadRequestException("This order is closed and can no longer be paid online.");
        }

        // Any state where money has already moved on this order. Taking more without a human
        // deciding is worse than refusing a diner who is most likely retrying a payment that
        // already worked.
        //
        // PartiallyPaid is in this list, and that is the deliberate part. Online payment charges
        // order.Total — the whole order — so letting a part-paid order through would redirect a
        // diner who already handed over CHF 20 at the till to a page for the full CHF 50. Charging
        // the BALANCE instead is not a smaller change than it looks: the balance would have to be
        // frozen for the 30 minutes the Stripe session is live, or a second till payment lands
        // mid-redirect and the diner overpays anyway. Settling a partial online payment is S5/S11
        // territory; refusing here is the honest v1 answer.
        if (order.PaymentStatus is PaymentStatus.Completed or PaymentStatus.Overpaid
            or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
            or PaymentStatus.PartiallyPaid)
        {
            throw new BadRequestException(
                "This order has already been partly or fully paid. Please settle it at the restaurant.");
        }
    }
}
