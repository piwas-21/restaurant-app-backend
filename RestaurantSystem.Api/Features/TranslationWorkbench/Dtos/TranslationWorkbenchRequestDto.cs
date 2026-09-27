namespace RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

public sealed record TranslationFieldInputDto(
    TranslationFieldRefDto FieldRef,
    string SourceLocale,
    string SourceText,
    Dictionary<string, string>? TargetTexts,
    TranslationContextDto? Context = null);

public sealed record TranslationContextDto(
    string? DishName,
    string? Category,
    List<string>? Exclusions);

public sealed record TranslationWorkbenchRequestDto(
    string GenerationIntent,
    List<string> TargetLocales,
    List<TranslationFieldInputDto> Fields);
