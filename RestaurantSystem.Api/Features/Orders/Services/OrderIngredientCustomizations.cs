using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Projects an order line's ingredient-quantities snapshot (<see cref="OrderItem.IngredientQuantitiesJson"/>,
/// a bare <c>Guid -&gt; int</c> map) against the recipe that line belongs to, producing the
/// per-ingredient rows the order screen and the kitchen ticket render.
/// <para>
/// Extracted from <see cref="OrderMappingService"/> rather than inlined, for the same reason
/// <see cref="OrderChildRendering"/> was: that service is already over its §4 limit, and the
/// reasoning here is longer than the code.
/// </para>
/// </summary>
internal static class OrderIngredientCustomizations
{
    /// <summary>
    /// Returns null when the line has no snapshot, no resolvable recipe, an unparseable one, or a
    /// snapshot that no longer resolves to ANY live ingredient row (see the remarks).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The name is the per-product one, never the global's (S0n).</b> It is read from the LIVE
    /// catalog row at render time, so preferring <c>GlobalIngredient.DefaultName</c> meant renaming
    /// a global ingredient silently reworded receipts and kitchen tickets for orders already
    /// placed. The owner settled it on 2026-08-24: a past receipt never changes. Until the
    /// <c>OrderItemIngredient</c> snapshot table of slice S1 exists there is nothing frozen to read,
    /// so the stop-gap is to at least say the same word the cart said — every guest-facing surface
    /// (customization sheet, MenuCard, POS sheet, cart line) renders <c>ProductIngredient.Name</c>.
    /// </para>
    /// <para>
    /// <b>A snapshot that resolves nothing says nothing.</b> <c>UpdateProductCommand</c> deletes and
    /// re-creates every <c>ProductIngredient</c> with a fresh <c>Guid</c> on each product save, so a
    /// line written before the last save matches none of the current ids. Those ids are then dropped
    /// in silence and every current base-recipe ingredient falls into the "absent = removed" branch
    /// below — printing "NO Cheese" for an ingredient nobody removed. Measured on prod 2026-08-27
    /// (slice S0m): 128 of 183 distinct ids are orphans, 80 of 98 snapshot-carrying lines resolve
    /// NOTHING, and 74 of those already render 147 false removals. Zero lines resolve only partly,
    /// which is the signature of a wholesale re-create rather than a per-ingredient edit.
    /// So an all-orphan snapshot yields no customization detail at all: silence is incomplete,
    /// a false removal is wrong. PARTIAL resolution is deliberately untouched — one surviving id
    /// means the snapshot is still about this recipe, and the existing rules apply.
    /// </para>
    /// </remarks>
    internal static List<OrderItemIngredientDto>? Map(OrderItem item, ILogger logger)
    {
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

        // The all-orphan guard. Logged at Debug, not Warning: on a tenant that has been running
        // through the id churn this is the NORMAL state of every historical line (80 of 98 on prod),
        // and the printer feed re-reads those lines on every poll — a warning here would be a flood,
        // not a signal. The count that matters is measured once, in the S0m research note.
        if (!productIngredients.Any(ing => selectedIngredients.ContainsKey(ing.Id)))
        {
            logger.LogDebug(
                "Order item {ItemId} carries {SavedCount} saved ingredient id(s), none matching a live "
                + "ProductIngredient row; reporting no customizations rather than false removals",
                item.Id,
                selectedIngredients.Count);
            return null;
        }

        // Show all ingredients for kitchen (both selected and removed).
        var customizations = new List<OrderItemIngredientDto>();
        foreach (var ing in productIngredients)
        {
            if (selectedIngredients.TryGetValue(ing.Id, out var quantity))
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
