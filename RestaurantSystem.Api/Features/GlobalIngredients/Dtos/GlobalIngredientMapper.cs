using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

internal static class GlobalIngredientMapper
{
    /// <param name="usedOnProductCount">
    /// From <c>GlobalIngredientUsage</c>. Defaults to 0 for the write paths, where the caller has
    /// just created the row and no product can reference it yet.
    /// </param>
    public static GlobalIngredientDto ToDto(GlobalIngredient ingredient, int usedOnProductCount = 0) => new()
    {
        Id = ingredient.Id,
        DefaultName = ingredient.DefaultName,
        ImageUrl = ingredient.ImageUrl,
        IsActive = ingredient.IsActive,
        Kind = ingredient.Kind,
        IsArchived = ingredient.ArchivedAt.HasValue,
        Origin = ingredient.Origin,
        UsedOnProductCount = usedOnProductCount,
        Translations = ingredient.Translations
            .Select(t => new GlobalIngredientTranslationDto
            {
                LanguageCode = t.LanguageCode,
                Name = t.Name,
            })
            .ToList(),
    };
}
