using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Produces the per-ingredient rows the order screen and the kitchen ticket render for one order
/// line, from the line's FROZEN snapshot (<see cref="OrderItem.IngredientSnapshots"/>) when it has
/// one, and otherwise by projecting its id map (<see cref="OrderItem.IngredientQuantitiesJson"/>,
/// a bare <c>Guid -&gt; int</c> map) against the recipe that line belongs to.
/// <para>
/// Extracted from <see cref="OrderMappingService"/> rather than inlined, for the same reason
/// <see cref="OrderChildRendering"/> was: that service is already over its §4 limit, and the
/// reasoning here is longer than the code.
/// </para>
/// </summary>
internal static class OrderIngredientCustomizations
{
    /// <summary>
    /// Returns null when the line has nothing to say: no snapshot and no id map, no resolvable
    /// recipe, an unparseable map, or a map that no longer resolves to ANY live ingredient row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A frozen snapshot wins, and it is read WITHOUT touching the catalog (S1).</b> Every other
    /// part of an order line is already a snapshot — product name, variation name, unit price, line
    /// total are frozen columns — and the ingredient half was the exception: bare ids re-resolved
    /// against the live catalog on every read, so an admin renaming or removing an ingredient
    /// rewrote receipts and kitchen tickets for orders already placed. Owner ruling, 2026-08-24: a
    /// past receipt never changes (SHARED-MODIFIERS-AND-SAUCES-PLAN D2).
    /// </para>
    /// <para>
    /// <b>The fallback below is not dead code and must not be deleted.</b> S1 shipped with NO
    /// backfill by design, so every order placed before it has no snapshot rows and renders through
    /// exactly the path it always did. It also covers a line whose snapshot was legitimately empty —
    /// which is the same set of cases in which this projection returns null anyway.
    /// </para>
    /// <para>
    /// <b>The fallback name is the per-product one, never the global's (S0n).</b> It is read from the
    /// LIVE catalog row at render time, so preferring <c>GlobalIngredient.DefaultName</c> meant
    /// renaming a global ingredient silently reworded receipts and kitchen tickets for orders already
    /// placed. That is what the snapshot above now settles for new orders; for historic rows the
    /// stop-gap remains to at least say the same word the cart said — every guest-facing surface
    /// (customization sheet, MenuCard, POS sheet, cart line) renders <c>ProductIngredient.Name</c>.
    /// </para>
    /// <para>
    /// <b>A map that resolves nothing says nothing.</b> Before S0, <c>UpdateProductCommand</c> deleted
    /// and re-created every <c>ProductIngredient</c> with a fresh <c>Guid</c> on each product save, so
    /// a line written before the last save matches none of the current ids. Those ids were then
    /// dropped in silence and every current base-recipe ingredient fell into the "absent = removed"
    /// branch below — printing "NO Cheese" for an ingredient nobody removed. Measured on prod
    /// 2026-08-27 (slice S0m): 128 of 183 distinct ids are orphans, 80 of 98 map-carrying lines
    /// resolve NOTHING, and 74 of those already render 147 false removals. Zero lines resolve only
    /// partly, which is the signature of a wholesale re-create rather than a per-ingredient edit.
    /// So an all-orphan map yields no customization detail at all: silence is incomplete, a false
    /// removal is wrong. PARTIAL resolution is deliberately untouched — one surviving id means the
    /// map is still about this recipe, and the existing rules apply.
    /// </para>
    /// </remarks>
    internal static List<OrderItemIngredientDto>? Map(OrderItem item, ILogger logger)
    {
        var frozen = FromSnapshot(item);
        if (frozen != null)
        {
            return frozen;
        }

        // Either the line's own Product, or — for a menu-backed line (e.g. Chief's Special) —
        // the product behind the menu's first item.
        var productIngredients = item.Product?.DetailedIngredients
            ?? item.Menu?.MenuItems?.FirstOrDefault()?.Product?.DetailedIngredients;

        if (string.IsNullOrEmpty(item.IngredientQuantitiesJson) || productIngredients == null)
        {
            return null;
        }

        Dictionary<Guid, int>? selectedIngredients;
        try
        {
            selectedIngredients = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<Guid, int>>(item.IngredientQuantitiesJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse ingredient quantities for order item {ItemId}", item.Id);
            return null;
        }

        if (selectedIngredients == null || selectedIngredients.Count == 0)
        {
            return null;
        }

        var projected = ProjectRecipe(productIngredients, selectedIngredients);
        if (projected == null)
        {
            // The all-orphan guard fired. Logged at Debug, not Warning: on a tenant that has been
            // running through the id churn this is the NORMAL state of every historical line (80 of
            // 98 on prod), and the printer feed re-reads those lines on every poll — a warning here
            // would be a flood, not a signal. The count that matters is measured once, in the S0m
            // research note.
            logger.LogDebug(
                "Order item {ItemId} carries {SavedCount} saved ingredient id(s), none matching a live "
                + "ProductIngredient row; reporting no customizations rather than false removals",
                item.Id,
                selectedIngredients.Count);
        }

        return projected;
    }

    /// <summary>
    /// The frozen rows, in the order they were rendered in at checkout, or null when this line
    /// carries no snapshot (every order placed before S1, which is not backfilled).
    /// </summary>
    private static List<OrderItemIngredientDto>? FromSnapshot(OrderItem item)
    {
        var snapshot = item.IngredientSnapshots;
        if (snapshot == null || snapshot.Count == 0)
        {
            return null;
        }

        return snapshot
            .OrderBy(row => row.SortOrder)
            .Select(row => new OrderItemIngredientDto
            {
                IngredientId = row.IngredientId,
                IngredientName = row.IngredientName,
                Quantity = row.Quantity,
                IsRemoved = row.IsRemoved
            })
            .ToList();
    }

    /// <summary>
    /// The one rule for "which ingredients does this line show, in what order, and which of them
    /// count as removed", projecting <paramref name="savedQuantities"/> against
    /// <paramref name="recipe"/>. Returns null when NOT ONE saved id resolves (see the remarks on
    /// <see cref="Map"/>).
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="OrderIngredientSnapshot"/>, which calls it at checkout to record what
    /// this method would render. Keeping the writer and the historic-fallback reader on ONE
    /// implementation is what makes the snapshot faithful rather than merely similar.
    /// </remarks>
    internal static List<OrderItemIngredientDto>? ProjectRecipe(
        IEnumerable<ProductIngredient> recipe,
        Dictionary<Guid, int> savedQuantities)
    {
        // ORDERED, and this is the whole of the fix for the freeze order.
        //
        // The sequence this method returns is what OrderIngredientSnapshot.Build indexes to assign
        // `OrderItemIngredient.SortOrder` (0,1,2…), and that snapshot is what a receipt and the
        // kitchen ticket render. The input reaches here as `Product.DetailedIngredients`, an EF
        // `.Include(...)` with no `OrderBy` on ANY call path, so the sequence was whatever Postgres
        // happened to return: `SortOrder` was populated and stable per line — a given order always
        // printed the same way — but WHICH ingredient became index 0 varied between two orders of
        // the SAME dish, and matched the recipe order the admin arranged in the editor only by
        // luck. #603 shipped drag-reordering to make `DisplayOrder` mean something; the freeze
        // ignored it.
        //
        // `ThenBy(Id)` is not decoration: `useVariationReorder` documents that live `DisplayOrder`
        // holds gaps AND DUPLICATES, so ordering by it alone still leaves ties, and a tie is where
        // this defect lives.
        //
        // Already-frozen rows are NOT backfilled. Each one is faithful to what was rendered at
        // checkout, and a receipt records what happened rather than what we would prefer it had
        // looked like.
        var recipeRows = recipe.OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id).ToList();

        if (!recipeRows.Exists(ing => savedQuantities.ContainsKey(ing.Id)))
        {
            return null;
        }

        // Show all ingredients for kitchen (both selected and removed).
        var customizations = new List<OrderItemIngredientDto>();
        foreach (var ing in recipeRows)
        {
            if (savedQuantities.TryGetValue(ing.Id, out var quantity))
            {
                // Ingredient is in the order - show it regardless of quantity. Whether a quantity
                // of 0 counts as a REMOVAL (→ a "NO X" kitchen-ticket line) is IngredientRecipeRules'
                // decision, shared since #363 with the cart, which must call a removal the same
                // thing this does. The rationale that used to sit here lives on that class.
                customizations.Add(new OrderItemIngredientDto
                {
                    IngredientId = ing.Id,
                    IngredientName = ing.Name,
                    Quantity = quantity,
                    IsRemoved = IngredientRecipeRules.IsRemoved(ing, quantity)
                });
            }
            else if (!ing.IsOptional)
            {
                // Required ingredient not in selection at all = removed. Reachable only when at
                // least one OTHER saved id resolved — the guard above rules out the case where this
                // branch would fire for the whole recipe.
                customizations.Add(new OrderItemIngredientDto
                {
                    IngredientId = ing.Id,
                    IngredientName = ing.Name,
                    Quantity = 0,
                    IsRemoved = true
                });
            }
        }

        return customizations;
    }
}
