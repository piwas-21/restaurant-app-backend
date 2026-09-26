using System.Text.RegularExpressions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Menus;

internal static partial class MenuSectionLocale
{
    [GeneratedRegex("^[a-z]{2,3}(?:-[a-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();

    public static bool IsValidTag(string languageCode) =>
        languageCode.Length <= 10 && LanguageTagPattern().IsMatch(languageCode);

    public static (string Name, string? Description) Resolve(
        MenuSection section,
        string? requestedLocale)
    {
        if (string.IsNullOrWhiteSpace(requestedLocale))
        {
            return (section.Name, section.Description);
        }

        var normalized = requestedLocale.Trim().Replace('_', '-');
        var translation = Find(section.Translations, normalized)
            ?? Find(section.Translations, normalized.Split('-', 2)[0]);

        return translation is null
            ? (section.Name, section.Description)
            : (translation.Name, translation.Description ?? section.Description);
    }

    public static Dictionary<string, MenuSectionTranslationDto> ToDto(
        IEnumerable<MenuSectionTranslation> translations) => translations
        .GroupBy(translation => translation.LanguageCode, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToDictionary(
            translation => translation.LanguageCode,
            translation => new MenuSectionTranslationDto
            {
                Name = translation.Name,
                Description = translation.Description
            },
            StringComparer.OrdinalIgnoreCase);

    private static MenuSectionTranslation? Find(
        IEnumerable<MenuSectionTranslation> translations,
        string languageCode) => translations.FirstOrDefault(
            translation => string.Equals(translation.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
}
