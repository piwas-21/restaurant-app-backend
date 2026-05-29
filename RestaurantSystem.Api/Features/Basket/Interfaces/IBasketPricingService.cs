namespace RestaurantSystem.Api.Features.Basket.Interfaces;

/// <summary>
/// Computes and applies the monetary totals on a <see cref="RestaurantSystem.Domain.Entities.Basket"/>
/// (sub-total, customer discount, tax, total). Operates on an already-loaded basket (with its
/// <c>Items</c> populated) and does NOT persist — the caller owns the DB load and save. Extracted
/// from <c>BasketService.RecalculateBasketTotalsAsync</c> (Sprint 3 god-class decomposition).
/// </summary>
public interface IBasketPricingService
{
    /// <summary>
    /// Recomputes <c>SubTotal</c>, <c>CustomerDiscount</c>, <c>Tax</c>, and <c>Total</c> on the
    /// given basket from its current items and the user's best applicable discount. Tax is left at
    /// 0 — it's calculated at order creation when the order type (and thus the Swiss rate) is known.
    /// </summary>
    Task ApplyTotalsAsync(RestaurantSystem.Domain.Entities.Basket basket, CancellationToken cancellationToken = default);
}
