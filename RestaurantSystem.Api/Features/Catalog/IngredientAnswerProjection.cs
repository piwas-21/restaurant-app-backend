using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

public static class IngredientAnswerProjection
{
    public static bool IsNoneOption(ProductIngredient ingredient) =>
        ingredient.GlobalIngredient is { IsDeleted: false, IsNoneOption: true };
}
