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

    /// <summary>
    /// Computes the price delta from optional-ingredient customisations for a product.
    /// For each optional, active ingredient: if it's included in the base price, a
    /// deselection deducts its price and quantities above 1 add the extra units; if it's
    /// not included, a selection adds <c>price × quantity</c>. Quantity is clamped to the
    /// ingredient's <c>MaxQuantity</c>. Pure calculation (no I/O); shared by the menu-option
    /// and regular-product add-to-basket paths.
    /// </summary>
    decimal CalculateIngredientCustomizationPrice(
        IEnumerable<RestaurantSystem.Domain.Entities.ProductIngredient>? detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredientIds,
        IReadOnlyDictionary<Guid, int>? ingredientQuantities);
}
