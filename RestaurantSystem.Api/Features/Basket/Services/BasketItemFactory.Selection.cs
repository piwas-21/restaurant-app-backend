using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

public partial class BasketItemFactory
{
    private static (List<Guid>? IngredientIds, Dictionary<Guid, int>? Quantities) ResolveIngredientSelection(
        Product product,
        ExplicitCustomizationResolution explicitSelection,
        List<Guid>? legacyIngredientIds,
        Dictionary<Guid, int>? legacyQuantities)
    {
        if (product.CustomizationGroups.Any(group => group.IsActive))
        {
            return (explicitSelection.SelectedIngredientIds, explicitSelection.IngredientQuantities);
        }

        return (legacyIngredientIds, legacyQuantities);
    }
}
