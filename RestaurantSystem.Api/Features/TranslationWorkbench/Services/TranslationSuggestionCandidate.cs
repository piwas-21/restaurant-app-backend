using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

internal sealed record TranslationSuggestionCandidate(
    TranslationFieldRefDto FieldRef,
    string SourceLocale,
    string Locale,
    string SourceText,
    string SourceHash,
    TranslationContextDto? Context,
    string ContextHash,
    string Fingerprint);
