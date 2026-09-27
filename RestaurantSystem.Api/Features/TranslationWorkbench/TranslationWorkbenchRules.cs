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
        ["product", "globalIngredient", "productIngredient", "productVariation", "menuSection", "optionSet", "category"];

    [GeneratedRegex(@"\{\{[^{}]+\}\}|\{[A-Za-z_][^{}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?|[$€£¥₺]", RegexOptions.CultureInvariant)]
    private static partial Regex NumberOrCurrencyPattern();

    public static void Validate(TranslationWorkbenchRequestDto request)
    {
        if (!IsValidRequestEnvelope(request))
        {
            throw new BadRequestException("Invalid translation workbench request");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        if (request.Fields.Any(field =>
            field is null || !IsValidField(field) || !identities.Add(Identity(field.FieldRef))))
        {
            throw new BadRequestException("Invalid or duplicate translation field");
        }
    }

    private static bool IsValidRequestEnvelope(TranslationWorkbenchRequestDto request) =>
        (request.GenerationIntent is "saveReview" or "explicitFill" or "explicitAlternative") &&
        request.TargetLocales is { Count: >= 1 and <= 10 } &&
        request.TargetLocales.Distinct(StringComparer.Ordinal).Count() == request.TargetLocales.Count &&
        request.TargetLocales.All(GuestLocales.Contains) &&
        request.Fields is { Count: >= 1 and <= 40 };

    private static bool IsValidField(TranslationFieldInputDto field)
    {
        var reference = field.FieldRef;
        return reference is not null && IsValidReference(reference) &&
            IsValidSource(field, reference) && IsValidTargets(field.TargetTexts, reference.FieldKey) &&
            IsValidContext(field.Context);
    }

    private static bool IsValidReference(TranslationFieldRefDto reference) =>
        EntityTypes.Contains(reference.EntityType) &&
        (reference.FieldKey is "name" or "description") &&
        !((reference.EntityType is "globalIngredient" or "productIngredient" or "optionSet") &&
            reference.FieldKey != "name");

    private static bool IsValidSource(TranslationFieldInputDto field, TranslationFieldRefDto reference) =>
        GuestLocales.Contains(field.SourceLocale) &&
        !string.IsNullOrWhiteSpace(field.SourceText) &&
        field.SourceText.Length <= MaxLength(reference.FieldKey) &&
        reference.EntityId.HasValue != !string.IsNullOrWhiteSpace(reference.ClientKey) &&
        (reference.ClientKey is null || reference.ClientKey.Length <= 128);

    private static bool IsValidTargets(Dictionary<string, string>? targets, string fieldKey) =>
        targets?.All(pair => GuestLocales.Contains(pair.Key) && pair.Value is not null &&
            pair.Value.Length <= MaxLength(fieldKey)) != false;

    private static bool IsValidContext(TranslationContextDto? context) =>
        (context?.DishName?.Length ?? 0) <= 200 && (context?.Category?.Length ?? 0) <= 100 &&
        context?.Exclusions is not { Count: > 20 } &&
        context?.Exclusions?.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 200) != false;

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
