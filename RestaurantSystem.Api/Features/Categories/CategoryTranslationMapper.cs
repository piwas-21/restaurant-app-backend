using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories;

internal static class CategoryTranslationMapper
{
    public static Dictionary<string, CategoryContentDto> ToDto(IEnumerable<CategoryTranslation> translations) =>
        translations.GroupBy(value => value.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(value => value.LanguageCode, value => new CategoryContentDto
            {
                Name = value.Name,
                Description = value.Description
            }, StringComparer.Ordinal);

    public static TranslationTextMap ToTextMap(Category category) => TranslationTextMap.Create(
        category.Name,
        category.Description,
        category.Translations.Select(value =>
            (value.LanguageCode, (string?)value.Name, value.Description)));

    public static string? NormalizeSourceLocale(string? sourceLocale)
    {
        if (sourceLocale is null) return null;
        var normalized = sourceLocale.Trim().ToLowerInvariant();
        if (!TranslationWorkbenchRules.GuestLocales.Contains(normalized))
        {
            throw new BadRequestException("The category source locale is not supported.");
        }

        return normalized;
    }

    public static void Replace(
        ApplicationDbContext context,
        Category category,
        IReadOnlyDictionary<string, CategoryContentDto>? translations,
        string actor)
    {
        if (translations is null) return;

        var normalized = Normalize(translations);
        context.CategoryTranslations.RemoveRange(category.Translations);
        category.Translations.Clear();
        var now = DateTime.UtcNow;
        foreach (var (locale, content) in normalized)
        {
            var row = new CategoryTranslation
            {
                Id = Guid.NewGuid(),
                Category = category,
                LanguageCode = locale,
                Name = content.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(content.Description) ? null : content.Description.Trim(),
                CreatedAt = now,
                CreatedBy = actor
            };
            category.Translations.Add(row);
            context.CategoryTranslations.Add(row);
        }
    }

    private static Dictionary<string, CategoryContentDto> Normalize(
        IReadOnlyDictionary<string, CategoryContentDto> translations)
    {
        if (translations.Count > TranslationWorkbenchRules.GuestLocales.Count)
        {
            throw new BadRequestException("A category may include at most ten locale translations.");
        }

        var normalized = new Dictionary<string, CategoryContentDto>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rawLocale, content) in translations)
        {
            var locale = rawLocale.Trim().ToLowerInvariant();
            if (!TranslationWorkbenchRules.GuestLocales.Contains(locale) || content is null || content.Name is null ||
                content.Name.Length > TranslationWorkbenchRules.MaxLength("name") ||
                content.Description?.Length > TranslationWorkbenchRules.MaxLength("description"))
            {
                throw new BadRequestException("Category translations contain an invalid locale or text value.");
            }

            if (!seen.Add(locale))
            {
                throw new BadRequestException($"Category translation locale '{locale}' is duplicated.");
            }

            if (!string.IsNullOrWhiteSpace(content.Name) || !string.IsNullOrWhiteSpace(content.Description))
            {
                normalized[locale] = content;
            }
        }

        return normalized;
    }
}
