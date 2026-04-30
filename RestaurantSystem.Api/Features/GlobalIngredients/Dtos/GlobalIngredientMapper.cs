using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

internal static class GlobalIngredientMapper
{
    public static GlobalIngredientDto ToDto(GlobalIngredient ingredient) => new()
    {
        Id = ingredient.Id,
        DefaultName = ingredient.DefaultName,
        ImageUrl = ingredient.ImageUrl,
        IsActive = ingredient.IsActive,
        Translations = ingredient.Translations
            .Select(t => new GlobalIngredientTranslationDto
            {
                LanguageCode = t.LanguageCode,
                Name = t.Name,
            })
            .ToList(),
    };
}
