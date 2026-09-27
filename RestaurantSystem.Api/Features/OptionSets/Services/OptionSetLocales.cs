using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static partial class OptionSetLocales
{
    private const int MaximumLocales = 10;

    [GeneratedRegex("^[a-z]{2,3}(?:-[a-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();

    public static string NormalizeLocale(string? locale)
    {
        var normalized = locale?.Trim().Replace('_', '-').ToLowerInvariant() ?? string.Empty;
        if (normalized.Length > 10 || !LanguageTagPattern().IsMatch(normalized))
        {
            throw new BadRequestException("Source and translation locales must be valid language tags up to 10 characters");
        }

        return normalized;
    }

    public static Dictionary<string, string> NormalizeTranslations(
        IReadOnlyDictionary<string, string>? translations)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (translations is null)
        {
            return result;
        }

        if (translations.Count > MaximumLocales)
        {
            throw new BadRequestException("An option set may contain translations for at most 10 locales");
        }

        foreach (var (language, value) in translations)
        {
            var locale = NormalizeLocale(language);
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 120 || !result.TryAdd(locale, value.Trim()))
            {
                throw new BadRequestException("Option-set translations need unique locales and names up to 120 characters");
            }
        }

        return result;
    }

    public static List<OptionSetTranslation> CreateEntities(
        OptionSet optionSet,
        IReadOnlyDictionary<string, string> translations,
        string actor,
        DateTime now) => translations.Select(pair => new OptionSetTranslation
        {
            Id = Guid.NewGuid(),
            OptionSet = optionSet,
            LanguageCode = pair.Key,
            Name = pair.Value,
            CreatedAt = now,
            CreatedBy = actor
        }).ToList();

    public static void Sync(
        ICollection<OptionSetTranslation> existing,
        IReadOnlyDictionary<string, string> translations,
        string actor,
        DateTime now)
    {
        var current = existing.ToDictionary(item => item.LanguageCode, StringComparer.OrdinalIgnoreCase);
        foreach (var (language, name) in translations)
        {
            if (current.TryGetValue(language, out var row))
            {
                if (row.Name != name)
                {
                    row.Name = name;
                    row.UpdatedAt = now;
                    row.UpdatedBy = actor;
                }
                continue;
            }

            existing.Add(new OptionSetTranslation
            {
                Id = Guid.NewGuid(),
                LanguageCode = language,
                Name = name,
                CreatedAt = now,
                CreatedBy = actor
            });
        }

        foreach (var stale in existing.Where(item => !translations.ContainsKey(item.LanguageCode)).ToArray())
        {
            existing.Remove(stale);
        }
    }

    public static Dictionary<string, string> ToDto(IEnumerable<OptionSetTranslation> translations) => translations
        .GroupBy(item => item.LanguageCode, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToDictionary(item => item.LanguageCode, item => item.Name, StringComparer.OrdinalIgnoreCase);
}
