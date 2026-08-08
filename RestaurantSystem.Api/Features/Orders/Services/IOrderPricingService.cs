using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Applies pricing to an in-memory order based on item totals, the
/// originating <see cref="CreateOrderCommand"/>, and the customer's
/// discount eligibility. Mutates the order in place — does not persist.
///
/// <para>
/// <b>The server is the only authority on what an order costs.</b> There is one path: every money
/// field is derived here from the order's own items, its type, and the customer's DB-resident
/// discount eligibility. The command carries no pre-calculated basket totals — it used to, and
/// <c>Total</c> was copied from the request body verbatim, so an anonymous caller could post
/// <c>basketTotal: 0</c> and receive a fully-paid order (S0b).
/// </para>
///
/// <para>
/// Tax is extracted from item prices for display only — it does not change what the customer pays
/// (Swiss VAT is price-inclusive). <c>Tip</c> is added <i>after</i> price rounding: the rounding
/// courtesy applies to the sale, never to an amount the customer chose to give.
/// </para>
///
/// Out of scope: subtotal aggregation (caller computes <c>itemsTotal</c> from <c>order.Items</c>)
/// and any persistence.
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

    /// <summary>
    /// Recomputes <c>Total</c> from the order's already-persisted money fields, picking up a
    /// <c>FidelityPointsDiscount</c> that only became known after the order was saved (redemption
    /// FKs the order, so it cannot run before the insert).
    /// <para>
    /// Does no DB work, and is idempotent <i>within a request</i> — it recomputes from the order's
    /// own columns rather than subtracting a delta, so calling it twice cannot double-discount.
    /// </para>
    /// <para>
    /// <b>Caveat for a reloaded order</b> (S5's settle path will do exactly that):
    /// <c>ApplyUserLimitDiscountAsync</c> stores <c>Discount</c> unrounded while the column is
    /// <c>decimal(10,2)</c>, so a round-trip can shift it by a sub-cent. With a discount active
    /// that is enough to cross <c>ApplySpecialRounding</c>'s <c>.10</c> boundary and move
    /// <c>Total</c> by a whole unit. Round the discount at assignment before relying on this
    /// across a reload.
    /// </para>
    /// </summary>
    void RecalculateTotal(Order order);
}
