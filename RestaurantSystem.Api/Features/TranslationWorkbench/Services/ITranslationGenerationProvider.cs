namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed record TranslationGenerationTarget(
    string Key,
    string SourceLocale,
    string Locale,
    string FieldKey,
    string SourceText,
    string? DishName,
    string? Category,
    IReadOnlyList<string> Exclusions);

public sealed record TranslationGenerationResult(
    IReadOnlyDictionary<string, string> Texts,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens);

public interface ITranslationGenerationProvider
{
    Task<TranslationGenerationResult> GenerateAsync(
        IReadOnlyList<TranslationGenerationTarget> targets,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken);
}
