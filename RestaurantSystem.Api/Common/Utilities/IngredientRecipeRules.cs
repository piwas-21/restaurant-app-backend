using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Utilities;

/// <summary>
/// The one place that answers "is this ingredient part of what you get at the base price, and does
/// a saved quantity of 0 therefore mean the guest REMOVED it?".
///
/// <para>It exists because 0 is ambiguous on its own, and reading it naively is a live defect
/// rather than a hypothetical one. When <c>LineCustomizationBuilder</c> backfills a line it writes
/// an explicit 0 for every unselected <b>active</b> optional-or-included ingredient, so a saved
/// pizza can carry a 0 for a paid topping nobody asked for. Treating each of those as a removal
/// prints "NO Extra Bacon" for something that was never on the dish — the spurious kitchen-ticket
/// noise <c>OrderMappingService</c>'s guard was added to stop, and which the cart would have
/// repeated when it started reading the same channel (#363).</para>
///
/// <para>The distinction is the BASE RECIPE: an ingredient is in it when it is required, or when it
/// is optional but included in the base price. Only those can be removed, because only those were
/// there to begin with.</para>
///
/// <para><b>Deliberately NOT the whole of either caller's removal logic.</b> Two things sit outside
/// it on purpose. <c>OrderMappingService</c> additionally treats a required ingredient that is
/// ABSENT from the saved map as removed, and the cart does not — so the two still disagree for a
/// required ingredient that was never written. And a caller must decide for itself whether a saved
/// map reflects a real choice at all: a backfilled line carries zeroes the guest never made, which
/// is why <c>BasketMappingService</c> gates on the line having carried a selection before reading
/// one. Neither belongs here, because neither is a fact about the ingredient.</para>
///
/// <para>The base-recipe condition has a frontend twin — <c>utils/ingredientSelection.ts</c>'s
/// <c>buildBaseIngredientSelection</c> decides what a freshly-opened customization preselects using
/// the same expression, which is what makes a default line price at exactly the base price. It
/// additionally skips inactive ingredients where this does not: that one reads the menu as it is
/// now, this one reads back what was saved, and an ingredient deactivated afterwards still has its
/// stored quantity. The shared half is the base-recipe test — change it in one, change it in the
/// other.</para>
/// </summary>
public static class IngredientRecipeRules
{
    /// <summary>
    /// True when the ingredient is part of what the base price buys: required, or optional but
    /// included in the base price.
    /// </summary>
    public static bool IsInBaseRecipe(ProductIngredient ingredient) =>
        !ingredient.IsOptional || ingredient.IsIncludedInBasePrice;

    /// <summary>
    /// True when <paramref name="quantity"/> means the guest removed <paramref name="ingredient"/>,
    /// rather than never having added it. Assumes the caller has established that the saved
    /// quantities reflect a real choice — see the class remarks.
    /// </summary>
    public static bool IsRemoved(ProductIngredient ingredient, int quantity) =>
        quantity == 0 && IsInBaseRecipe(ingredient);
}
