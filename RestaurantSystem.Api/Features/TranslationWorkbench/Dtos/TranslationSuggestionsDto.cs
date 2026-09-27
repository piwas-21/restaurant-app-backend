namespace RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

public sealed record TranslationSuggestionDto(
    Guid SuggestionId,
    TranslationFieldRefDto FieldRef,
    string Locale,
    string SourceHash,
    string Text,
    string Provider,
    string Model,
    string Status);

public sealed record TranslationGapDto(TranslationFieldRefDto FieldRef, string Locale, string Reason);

public sealed record TranslationSuggestionsDto(
    IReadOnlyList<TranslationSuggestionDto> Suggestions,
    IReadOnlyList<TranslationGapDto> Skipped,
    string ProviderStatus);
