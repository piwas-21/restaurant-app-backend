namespace RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

public sealed record TranslationProvenanceDto(
    string Kind,
    string? TemplateId,
    int? TemplateRevision,
    string? SourceHash,
    string ReviewStatus,
    string? ReviewerId,
    DateTime? ReviewedAt);

public sealed record TranslationTargetStatusDto(
    string Locale,
    string Status,
    string? Text,
    TranslationProvenanceDto? Provenance);

public sealed record TranslationFieldStatusDto(
    TranslationFieldRefDto FieldRef,
    string SourceLocale,
    string SourceHash,
    string SourceText,
    IReadOnlyList<TranslationTargetStatusDto> Targets);

public sealed record TranslationPreviewDto(IReadOnlyList<TranslationFieldStatusDto> Rows);
