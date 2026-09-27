using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

internal static class TranslationContentVersion
{
    public static string ForProduct(Product product) => Compute(product.Name, product.Description,
        product.Descriptions.Select(row => (row.Lang, row.Name, (string?)row.Description)));

    public static string ForCategory(Category category) => ForCategory(category.SourceLocale, category.Name,
        category.Description, category.Translations.Select(row =>
            (row.LanguageCode, row.Name, row.Description)));

    public static string ForCategory(
        string? sourceLocale,
        string name,
        string? description,
        IEnumerable<(string Locale, string Name, string? Description)> content) =>
        TranslationWorkbenchRules.Hash(JsonSerializer.Serialize(new
        {
            sourceLocale,
            name,
            description,
            content = content.OrderBy(row => row.Locale, StringComparer.Ordinal)
                .Select(row => new { row.Locale, row.Name, row.Description }).ToArray()
        }));

    public static string Compute(
        string name,
        string? description,
        IEnumerable<(string Locale, string Name, string? Description)> content) =>
        TranslationWorkbenchRules.Hash(JsonSerializer.Serialize(new
        {
            name,
            description,
            content = content.OrderBy(row => row.Locale, StringComparer.Ordinal)
                .Select(row => new { row.Locale, row.Name, row.Description }).ToArray()
        }));

    public static void EnsureCurrent(Product product, string? expectedVersion, int acceptedCount)
    {
        if (acceptedCount == 0) return;
        if (string.IsNullOrWhiteSpace(expectedVersion) ||
            !string.Equals(expectedVersion, ForProduct(product), StringComparison.Ordinal))
        {
            throw new ConflictException("Menu text changed. Reload before saving reviewed translations.");
        }
    }

    public static void EnsureCurrent(Category category, string? expectedVersion, int acceptedCount)
    {
        if (acceptedCount == 0) return;
        if (string.IsNullOrWhiteSpace(expectedVersion) ||
            !string.Equals(expectedVersion, ForCategory(category), StringComparison.Ordinal))
        {
            throw new ConflictException("Category text changed. Reload before saving reviewed translations.");
        }
    }
}
