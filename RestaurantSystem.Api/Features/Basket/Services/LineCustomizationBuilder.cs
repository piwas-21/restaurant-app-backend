using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// The typed ingredient-customization state for one basket line: the persisted columns plus the
/// ingredient customization price. One writer for both the regular-item and bundle-child paths
/// (menu-bundles redesign #155), so a new customization field can never again be added to one
/// branch and forgotten in the other.
/// </summary>
public sealed record LineCustomization(
    List<Guid>? SelectedIngredients,
    string? IngredientQuantitiesJson,
    decimal CustomizationPrice);

public interface ILineCustomizationBuilder
{
    /// <param name="preferProvidedQuantities">
    /// Selects the (historically divergent, behaviour-preserved) quantity-JSON precedence:
    /// <c>true</c> = the regular-item rule (an explicit client map is persisted verbatim, else
    /// backfill from the selection); <c>false</c> = the bundle-child rule (backfill from the
    /// selection when present, else persist a provided map as-is).
    /// </param>
    LineCustomization Build(
        ICollection<ProductIngredient>? detailedIngredients,
        List<Guid>? selectedIngredients,
        Dictionary<Guid, int>? ingredientQuantities,
        bool preferProvidedQuantities);
}

public class LineCustomizationBuilder : ILineCustomizationBuilder
{
    private readonly IBasketPricingService _basketPricingService;

    public LineCustomizationBuilder(IBasketPricingService basketPricingService)
    {
        _basketPricingService = basketPricingService;
    }

    public LineCustomization Build(
        ICollection<ProductIngredient>? detailedIngredients,
        List<Guid>? selectedIngredients,
        Dictionary<Guid, int>? ingredientQuantities,
        bool preferProvidedQuantities)
    {
        var customizationPrice = _basketPricingService.CalculateIngredientCustomizationPrice(
            detailedIngredients, selectedIngredients, ingredientQuantities);

        var ingredientQuantitiesJson = BuildIngredientQuantitiesJson(
            detailedIngredients, selectedIngredients, ingredientQuantities, preferProvidedQuantities);

        return new LineCustomization(selectedIngredients, ingredientQuantitiesJson, customizationPrice);
    }

    private static string? BuildIngredientQuantitiesJson(
        ICollection<ProductIngredient>? detailedIngredients,
        List<Guid>? selectedIngredients,
        Dictionary<Guid, int>? ingredientQuantities,
        bool preferProvidedQuantities)
    {
        if (preferProvidedQuantities)
        {
            // Regular-item precedence (was BuildRegularItemAsync): an explicit client map is
            // persisted verbatim; only without one do we backfill from the selection.
            if (ingredientQuantities is { Count: > 0 })
            {
                return JsonSerializer.Serialize(ingredientQuantities);
            }

            if (detailedIngredients is { Count: > 0 })
            {
                var built = BuildIngredientQuantities(detailedIngredients, selectedIngredients, ingredientQuantities);
                return built.Count > 0 ? JsonSerializer.Serialize(built) : null;
            }

            return null;
        }

        // Bundle-child precedence (was the BuildMenuItemAsync child loop): backfill from the
        // selection when present (so a deselected optional's "NO xxx" always reaches the kitchen
        // ticket), otherwise persist a provided map as-is.
        if (selectedIngredients != null && detailedIngredients is { Count: > 0 })
        {
            var built = BuildIngredientQuantities(detailedIngredients, selectedIngredients, ingredientQuantities);
            return built.Count > 0 ? JsonSerializer.Serialize(built) : null;
        }

        if (ingredientQuantities is { Count: > 0 })
        {
            return JsonSerializer.Serialize(ingredientQuantities);
        }

        return null;
    }

    /// <summary>
    /// Builds the per-ingredient quantity map persisted as <c>IngredientQuantitiesJson</c>.
    /// A client-provided quantity wins; otherwise a selected ingredient gets quantity 1 and a
    /// deselected optional / included-in-base ingredient gets an explicit quantity 0, so the
    /// kitchen ticket can print "NO xxx" (OrderMappingService derives IsRemoved from 0).
    /// Non-optional ingredients missing from the selection are implicitly included (no entry).
    /// </summary>
    private static Dictionary<Guid, int> BuildIngredientQuantities(
        IEnumerable<ProductIngredient> detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredients,
        Dictionary<Guid, int>? providedQuantities)
    {
        var selectedIngredientIds = selectedIngredients != null
            ? new HashSet<Guid>(selectedIngredients)
            : new HashSet<Guid>();
        var builtQuantities = new Dictionary<Guid, int>();

        foreach (var ingredient in detailedIngredients.Where(i => i.IsActive))
        {
            if (providedQuantities != null && providedQuantities.TryGetValue(ingredient.Id, out var quantity))
            {
                // Explicit client quantity (e.g. double cheese) takes precedence.
                builtQuantities[ingredient.Id] = quantity;
            }
            else if (selectedIngredientIds.Contains(ingredient.Id))
            {
                // Selected ingredient without an explicit quantity: quantity 1
                builtQuantities[ingredient.Id] = 1;
            }
            else if (ingredient.IsOptional || ingredient.IsIncludedInBasePrice)
            {
                // Optional ingredient not selected: mark as deselected (quantity 0)
                builtQuantities[ingredient.Id] = 0;
            }
            // Non-optional ingredients that are not selected are implicitly included
        }

        return builtQuantities;
    }
}
