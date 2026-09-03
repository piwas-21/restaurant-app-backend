using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.GlobalVariations.Dtos;

internal static class GlobalVariationMapper
{
    /// <param name="usedOnProductCount">
    /// From <c>GlobalVariationUsage</c>. Defaults to 0 for the create path, where no product can
    /// reference the row yet.
    /// </param>
    public static GlobalVariationDto ToDto(GlobalVariation variation, int usedOnProductCount = 0) => new()
    {
        Id = variation.Id,
        DefaultName = variation.DefaultName,
        IsActive = variation.IsActive,
        IsArchived = variation.ArchivedAt.HasValue,
        Origin = variation.Origin,
        UsedOnProductCount = usedOnProductCount,
        Translations = variation.Translations
            .Select(t => new GlobalVariationTranslationDto
            {
                LanguageCode = t.LanguageCode,
                Name = t.Name,
            })
            .ToList(),
    };
}
