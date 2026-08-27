using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Builds the frozen ingredient lines of an order row at checkout (slice S1 of
/// SHARED-MODIFIERS-AND-SAUCES-PLAN, decision D2 — a past receipt never changes).
/// </summary>
/// <remarks>
/// <para>
/// It records what the READ path would have rendered at that instant, by calling the very projection
/// the read path uses (<see cref="OrderIngredientCustomizations.ProjectRecipe"/>). That is not a
/// tidiness choice: a second, parallel implementation of "which ingredients does this line show, in
/// what order, and which of them count as removed" would drift, and the drift would only ever be
/// visible on orders already placed. One rule, two consumers.
/// </para>
/// <para>
/// <b>No price is recorded here, and that is a decision, not an omission.</b> Ingredient money has
/// exactly one writer — <c>BasketPricingService.CalculateIngredientCustomizationPrice</c>
/// (BasketPricingService.cs:97-159) — and what reaches an order is already an AGGREGATE: the basket
/// line's customization is folded to a single number at BasketToOrderTranslator.cs:137-138, passed
/// through OrderItemFactory.ResolveCustomizationPrice (OrderItemFactory.cs:242-243, which zeroes it
/// on an untrusted caller) and added to <c>OrderItem.ItemTotal</c>. There is no per-ingredient charge
/// anywhere on the order to copy. Deriving one here would mean re-reading
/// <c>ProductIngredient.Price</c> at checkout and re-running the arithmetic — a SECOND price
/// authority whose sum need not equal what was actually charged. No read surface asks for it either:
/// <c>OrderItemIngredientDto</c> (Dtos/OrderItemDto.cs:5-11) has no price field.
/// </para>
/// </remarks>
internal static class OrderIngredientSnapshot
{
    /// <summary>
    /// Projects <paramref name="savedQuantities"/> against <paramref name="recipe"/> and returns the
    /// rows to persist alongside the line. Empty means "nothing to freeze" — which is exactly the
    /// case in which the read path also renders nothing, so a historic-style fallback is correct.
    /// </summary>
    internal static List<OrderItemIngredient> Build(
        IEnumerable<ProductIngredient>? recipe,
        Dictionary<Guid, int>? savedQuantities,
        string createdBy)
    {
        if (recipe == null || savedQuantities == null || savedQuantities.Count == 0)
        {
            return [];
        }

        var rendered = OrderIngredientCustomizations.ProjectRecipe(recipe, savedQuantities);
        if (rendered == null)
        {
            return [];
        }

        var createdAt = DateTime.UtcNow;
        return rendered
            .Select((row, index) => new OrderItemIngredient
            {
                IngredientId = row.IngredientId,
                IngredientName = row.IngredientName,
                Quantity = row.Quantity,
                IsRemoved = row.IsRemoved,
                SortOrder = index,
                CreatedAt = createdAt,
                CreatedBy = createdBy,
            })
            .ToList();
    }
}
