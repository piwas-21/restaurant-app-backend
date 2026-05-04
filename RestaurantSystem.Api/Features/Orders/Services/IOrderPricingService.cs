using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Applies pricing to an in-memory order based on item totals, the
/// originating <see cref="CreateOrderCommand"/>, and the customer's
/// discount eligibility. Mutates the order in place — does not persist.
///
/// Two top-level paths preserved verbatim from <c>CreateOrderCommandHandler</c>:
///   1. Pre-calculated basket values are present on the command — copy
///      them onto the order (no DB lookups).
///   2. Otherwise compute Tax (extracted from item prices for display),
///      DeliveryFee, user-limit discount, and customer discount.
/// Total is then computed (or copied from <c>BasketTotal</c>) and
/// price-rounded via <see cref="Common.Utilities.PriceRoundingUtility"/>.
///
/// Out of scope: subtotal aggregation (caller computes
/// <c>itemsTotal</c> from <c>order.Items</c>), fidelity-points logic
/// (handler still owns that), and any persistence.
///
/// Extracted from <c>CreateOrderCommandHandler</c> in Sprint 2 task 2.9.
/// </summary>
public interface IOrderPricingService
{
    /// <summary>
    /// Mutates: <c>Tax</c>, <c>SubTotal</c>, <c>DeliveryFee</c>,
    /// <c>Discount</c>, <c>DiscountPercentage</c>,
    /// <c>CustomerDiscountAmount</c>, <c>CustomerDiscountRuleId</c>,
    /// <c>Total</c>.
    /// </summary>
    Task ApplyAsync(
        Order order,
        decimal itemsTotal,
        CreateOrderCommand command,
        Guid? userId,
        CancellationToken cancellationToken);
}
