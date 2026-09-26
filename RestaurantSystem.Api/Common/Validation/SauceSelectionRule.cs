using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using System.Text.Json;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Enforces a product's sauce-row cap at every server-side ingredient-selection writer.
/// A sauce MAX counts distinct active recipe rows, never array duplicates or row quantity.
/// </summary>
public static class SauceSelectionRule
{
    public const string MaximumExceededMessage = "The selected sauces exceed this item's maximum";
    public const string MinimumNotMetMessage = "The selected sauces do not meet this item's minimum";

    /// <summary>Checks a persisted basket line whose product and ingredient rows were loaded.</summary>
    public static void EnsureWithinMaximum(BasketItem line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Product is null)
        {
            return;
        }

        EnsureWithinMaximum(line.Product.DetailedIngredients, line.SelectedIngredients, line.Product.SauceMax);
    }

    public static void EnsureWithinMaximum(
        IEnumerable<ProductIngredient>? detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredientIds,
        int? sauceMax)
    {
        if (sauceMax is null || selectedIngredientIds is null)
        {
            return;
        }

        var selected = selectedIngredientIds.ToHashSet();
        var selectedSauceCount = (detailedIngredients ?? [])
            .Where(ingredient => ingredient.IsActive && ingredient.Kind == IngredientKind.Sauce)
            .Count(ingredient => selected.Contains(ingredient.Id));

        if (selectedSauceCount > sauceMax.Value)
        {
            throw new BadRequestException(MaximumExceededMessage, ErrorCodes.SauceMaximumExceeded);
        }
    }

    /// <summary>
    /// Optionally enforces a product's minimum sauce count. An explicit selection list is
    /// authoritative; older clients that send only a quantity map are supported by counting its
    /// positive sauce entries. Repeated IDs or quantities on one sauce never satisfy multiple
    /// distinct choices.
    /// </summary>
    public static void EnsureAtLeastMinimum(
        IEnumerable<ProductIngredient>? detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredientIds,
        IReadOnlyDictionary<Guid, int>? ingredientQuantities,
        int sauceMin,
        bool enforcementEnabled)
    {
        if (!enforcementEnabled || sauceMin <= 0)
        {
            return;
        }

        var sauceIds = (detailedIngredients ?? [])
            .Where(ingredient => ingredient.IsActive && ingredient.Kind == IngredientKind.Sauce)
            .Select(ingredient => ingredient.Id)
            .ToHashSet();
        var selectedIds = selectedIngredientIds is not null
            ? selectedIngredientIds.ToHashSet()
            : (ingredientQuantities ?? new Dictionary<Guid, int>())
                .Where(selection => selection.Value > 0)
                .Select(selection => selection.Key)
                .ToHashSet();

        if (sauceIds.Count(selectedIds.Contains) < sauceMin)
        {
            throw new BadRequestException(MinimumNotMetMessage, ErrorCodes.SauceMinimumNotMet);
        }
    }

    /// <summary>Checks a persisted basket line, including legacy rows with only quantity JSON.</summary>
    public static void EnsureAtLeastMinimum(BasketItem line, bool enforcementEnabled)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!enforcementEnabled || line.Product is null || line.Product.SauceMin <= 0)
        {
            return;
        }

        var quantities = line.SelectedIngredients is not null
            ? null
            : ReadQuantities(line.IngredientQuantitiesJson);
        EnsureAtLeastMinimum(
            line.Product.DetailedIngredients,
            line.SelectedIngredients,
            quantities,
            line.Product.SauceMin,
            enforcementEnabled: true);
    }

    private static Dictionary<Guid, int>? ReadQuantities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, int>>(json);
        }
        catch (JsonException)
        {
            // An unreadable legacy map cannot prove the required choices were made. Treat it as
            // absent, so enabled enforcement fails closed with the stable minimum-selection code.
            return null;
        }
    }
}
