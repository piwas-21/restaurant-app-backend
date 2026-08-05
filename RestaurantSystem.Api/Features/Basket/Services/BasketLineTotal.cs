using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// The one rule for a ROOT basket line's <c>ItemTotal</c> (#308).
///
/// Two shapes of root row are priced differently, because <c>BasketItemFactory</c> builds them
/// differently:
///
/// <list type="bullet">
/// <item><c>BuildRegularItemAsync</c> keeps customization OUT of <c>UnitPrice</c>, so the total is
/// <c>(UnitPrice + CustomizationPrice) * Quantity</c>.</item>
/// <item><c>BuildMenuItemAsync</c> folds it IN — its last three statements set
/// <c>UnitPrice = menuTotalPrice + totalCustomizationPrice</c> and then keep a copy in
/// <c>CustomizationPrice</c> for display — so the total is <c>UnitPrice * Quantity</c>, and adding
/// <c>CustomizationPrice</c> again charges the customization twice.</item>
/// </list>
///
/// Three sites recompute a root line after its quantity moves, and each had hard-coded ONE of the
/// two formulas, so each was wrong for the shape it did not have in mind: the login merge
/// double-charged a bundle (measured 57.00 against 48.00 on a 3 x 16.00 line), while the add-path
/// dedup and the cart stepper dropped a regular item's customization entirely (measured 25.98 for
/// two 15.98 lines, and 38.97 against 47.94).
///
/// Children are NOT covered: a child carries <c>ItemTotal = 0</c> by design so it cannot
/// double-count against its parent (see <see cref="BundleChildQuantityScaler"/>). This is for root
/// rows in a BASKET only. <c>OrderItemFactory</c> keeps its own convention —
/// <c>(UnitPrice * Quantity) + CustomizationPrice</c>, where the customization is line-absolute
/// rather than per-unit — and the two are reconciled at the seam by
/// <c>BasketToOrderTranslator.LineAbsoluteCustomization</c>, which derives that field from the line
/// total this rule produces (#312). So an order line equals its basket line wherever
/// <c>OrderItemFactory</c> echoes the DTO's <c>UnitPrice</c> — which needs BOTH a null <c>MenuId</c>
/// (<c>AddItemAsync</c> dispatches on it before <c>ResolvePricing</c> is ever reached) AND
/// <c>UnitPrice &gt; 0</c>. Neither condition is reachable from a basket today; see
/// <c>LineAbsoluteCustomization</c>'s remarks for both. Changing either formula without the other
/// reopens the divergence.
/// </summary>
public static class BasketLineTotal
{
    /// <summary>
    /// The line total for <paramref name="row"/> at its CURRENT <c>Quantity</c> — assign the new
    /// quantity first.
    /// </summary>
    /// <param name="loadedChildCount">
    /// The row's child rows AS LOADED BY THE CALLER. Load-bearing, and silent if wrong: an
    /// un-included <c>ChildBasketItems</c> reads as an empty collection rather than throwing, so a
    /// caller that forgets to load them prices every bundle as a regular item and double-charges its
    /// customization. Every call site pins this with a test that fails if the load is dropped.
    /// </param>
    public static decimal ForRoot(BasketItem row, int loadedChildCount)
    {
        ArgumentNullException.ThrowIfNull(row);

        return loadedChildCount > 0
            ? row.UnitPrice * row.Quantity
            : (row.UnitPrice + row.CustomizationPrice) * row.Quantity;
    }

    // WHY THE CHILD COUNT AND NOT THE PRODUCT TYPE.
    //
    // `Product.Type == ProductType.Menu` looks like the causal fact — it is what BasketService
    // branches on when it picks a factory method — but it is causal only at BUILD time, and it is
    // MUTABLE: UpdateProductCommand does an unguarded `product.Type = command.Type` with nothing
    // checking for live basket rows, and it handles the Menu -> non-Menu direction explicitly, so
    // the flip is supported rather than theoretical. A row's origin is therefore NOT recoverable
    // from the product's current type, and trusting it regresses rows that priced correctly before
    // this fix: retype Menu -> MainItem and a stale bundle parent stops returning early and falls
    // into the add-path dedup, which would then charge (16.00 + 3.00) * 2 = 38.00 where 32.00 is
    // right. An earlier draft of this file used the type as one operand of an `||` and claimed the
    // other operand's soundness as if it covered both; it did not.
    //
    // The child count has no such failure. Only BuildMenuItemAsync ever sets ParentBasketItemId, so
    // children present is proof of a bundle parent and can never be a false positive. That leaves
    // the false NEGATIVE — a bundle parent with no children — which is closed by an invariant plus
    // a guard:
    //
    //   * Built with no options: CustomizationPrice is accumulated ONLY inside the child loop
    //     (`totalCustomizationPrice += ... * option.Quantity`), so no children implies
    //     CustomizationPrice == 0, and there the two formulas agree — the answer is the same either
    //     way, so the misclassification cannot cost anything.
    //   * Children removed afterwards: this is why RemoveItemFromBasketAsync now refuses a child id
    //     (#308, mirroring #310 on the update path). Deleting a bundle's children individually was
    //     the one way to manufacture a parent with CustomizationPrice > 0 and no children — the
    //     single state this rule would get wrong. Closing that producer is what makes the rule
    //     total, so do NOT reopen it without revisiting this.
}
