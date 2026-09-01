using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Enforces a product's sauce-row cap at every server-side ingredient-selection writer.
/// A sauce MAX counts distinct active recipe rows, never array duplicates or row quantity.
/// </summary>
public static class SauceSelectionRule
{
    public const string MaximumExceededMessage = "The selected sauces exceed this item's maximum";

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
}
