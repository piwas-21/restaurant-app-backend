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

        // Overpaid and the two refund states are here as well as Completed: each one means money
        // has already moved on this order, and taking more without a human deciding is worse than
        // refusing a diner who is probably retrying a payment that already worked.
        if (order.PaymentStatus is PaymentStatus.Completed or PaymentStatus.Overpaid
            or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new BadRequestException("This order has already been paid.");
        }
    }
}
