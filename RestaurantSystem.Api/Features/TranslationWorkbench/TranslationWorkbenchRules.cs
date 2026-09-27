using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench;

internal static partial class TranslationWorkbenchRules
{
    public static readonly HashSet<string> GuestLocales =
        ["en", "tr", "es", "ar", "de", "fr", "nl", "it", "ru", "zh"];
    private static readonly HashSet<string> EntityTypes =
        ["product", "globalIngredient", "productIngredient", "productVariation", "menuSection", "optionSet"];

    [GeneratedRegex(@"\{\{[^{}]+\}\}|\{[A-Za-z_][^{}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?|[$€£¥₺]", RegexOptions.CultureInvariant)]
    private static partial Regex NumberOrCurrencyPattern();

    public static void Validate(TranslationWorkbenchRequestDto request)
    {
        if (request.GenerationIntent is not ("saveReview" or "explicitFill" or "explicitAlternative") ||
            request.TargetLocales is null or { Count: < 1 or > 10 } ||
            request.TargetLocales.Distinct(StringComparer.Ordinal).Count() != request.TargetLocales.Count ||
            request.TargetLocales.Any(locale => !GuestLocales.Contains(locale)) ||
            request.Fields is null or { Count: < 1 or > 40 })
        {
            throw new BadRequestException("Invalid translation workbench request");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in request.Fields)
        {
            if (field is null)
            {
                throw new BadRequestException("Invalid translation field");
            }
            var reference = field.FieldRef;
            if (reference is null || !EntityTypes.Contains(reference.EntityType) ||
                reference.FieldKey is not ("name" or "description") ||
                reference.EntityType is "globalIngredient" or "productIngredient" or "optionSet" &&
                    reference.FieldKey != "name" ||
                !GuestLocales.Contains(field.SourceLocale) ||
                string.IsNullOrWhiteSpace(field.SourceText) || field.SourceText.Length > MaxLength(reference.FieldKey) ||
                (reference.EntityId.HasValue == !string.IsNullOrWhiteSpace(reference.ClientKey)) ||
                reference.ClientKey?.Length > 128 ||
                field.TargetTexts?.Any(pair => !GuestLocales.Contains(pair.Key) ||
                    pair.Value is null || pair.Value.Length > MaxLength(reference.FieldKey)) == true ||
                field.Context?.DishName?.Length > 200 || field.Context?.Category?.Length > 100 ||
                field.Context?.Exclusions is { Count: > 20 } ||
                field.Context?.Exclusions?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200) == true ||
                !identities.Add(Identity(reference)))
            {
                throw new BadRequestException("Invalid or duplicate translation field");
            }
        }
    }

    public static int MaxLength(string fieldKey) => fieldKey == "name" ? 200 : 1000;

    public static string Identity(TranslationFieldRefDto field) =>
        $"{field.EntityType}|{field.EntityId?.ToString() ?? field.ClientKey}|{field.FieldKey}";

    public static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC))));

    public static string ContextHash(
        TranslationContextDto? context,
        IReadOnlyDictionary<string, string> glossary,
        string promptVersion,
        string model) => Hash(JsonSerializer.Serialize(new
        {
            Context = context,
            Glossary = glossary.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            PromptVersion = promptVersion,
            Model = model
        }));

    public static bool IsSafeSuggestion(string source, string suggestion, string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(suggestion) || suggestion.Length > MaxLength(fieldKey) ||
            suggestion.Any(char.IsControl))
        {
            return false;
        }

        var sourcePlaceholders = PlaceholderPattern().Matches(source).Select(match => match.Value)
            .OrderBy(value => value, StringComparer.Ordinal);
        var targetPlaceholders = PlaceholderPattern().Matches(suggestion).Select(match => match.Value)
            .OrderBy(value => value, StringComparer.Ordinal);
        if (!sourcePlaceholders.SequenceEqual(targetPlaceholders, StringComparer.Ordinal))
        {
            return false;
        }

        var sourceNumbers = NumberOrCurrencyPattern().Matches(source).Select(match => match.Value)
            .OrderBy(value => value, StringComparer.Ordinal);
        var targetNumbers = NumberOrCurrencyPattern().Matches(suggestion).Select(match => match.Value)
            .OrderBy(value => value, StringComparer.Ordinal);
        return sourceNumbers.SequenceEqual(targetNumbers, StringComparer.Ordinal);
    }
}
