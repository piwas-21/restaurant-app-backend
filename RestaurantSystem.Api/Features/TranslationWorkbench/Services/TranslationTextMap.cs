using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed record TranslationTextMap(
    string SourceName,
    string? SourceDescription,
    IReadOnlyDictionary<string, string?> Names,
    IReadOnlyDictionary<string, string?> Descriptions)
{
    public static TranslationTextMap Create(
        string name,
        string? description,
        IEnumerable<(string Locale, string? Name, string? Description)> translations)
    {
        var values = translations.ToArray();
        return new TranslationTextMap(name, description,
            values.ToDictionary(value => value.Locale, value => value.Name, StringComparer.Ordinal),
            values.ToDictionary(value => value.Locale, value => value.Description, StringComparer.Ordinal));
    }

    public static TranslationTextMap FromSection(MenuSectionDto section) => Create(
        section.Name, section.Description,
        (section.Translations ?? []).Select(pair =>
            (pair.Key, (string?)pair.Value.Name, pair.Value.Description)));

    public static TranslationTextMap FromCategory(Category category) => Create(
        category.Name, category.Description,
        category.Translations.Select(row =>
            (row.LanguageCode, (string?)row.Name, row.Description)));
}
