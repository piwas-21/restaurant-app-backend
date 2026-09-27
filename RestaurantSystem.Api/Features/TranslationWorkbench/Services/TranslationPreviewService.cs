using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed class TranslationPreviewService(
    ApplicationDbContext context,
    ITranslationTextReader textReader) : ITranslationPreviewService
{
    public async Task<TranslationPreviewDto> PreviewAsync(
        TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken)
    {
        TranslationWorkbenchRules.Validate(request);
        var ids = request.Fields.Select(field => field.FieldRef.EntityId)
            .OfType<Guid>().Distinct().ToArray();
        var provenance = await context.TranslationFieldProvenances.AsNoTracking()
            .Where(row => ids.Contains(row.EntityId))
            .ToListAsync(cancellationToken);
        var provenanceByKey = provenance.ToDictionary(row =>
            (row.EntityType, row.EntityId, row.FieldKey, row.Locale));
        var rows = new List<TranslationFieldStatusDto>(request.Fields.Count);

        foreach (var field in request.Fields)
        {
            var sourceHash = TranslationWorkbenchRules.Hash($"{field.SourceLocale}\n{field.SourceText}");
            var texts = await textReader.ReadAsync(field, cancellationToken);
            var targets = new List<TranslationTargetStatusDto>(request.TargetLocales.Count);
            foreach (var locale in request.TargetLocales)
            {
                texts.TryGetValue(locale, out var text);
                if (locale == field.SourceLocale && string.IsNullOrWhiteSpace(text))
                {
                    text = field.SourceText;
                }

                TranslationFieldProvenance? evidence = null;
                if (field.FieldRef.EntityId is Guid id)
                {
                    provenanceByKey.TryGetValue((field.FieldRef.EntityType, id, field.FieldRef.FieldKey, locale),
                        out evidence);
                }

                var evidenceMatchesText = evidence is not null &&
                    !string.IsNullOrWhiteSpace(text) &&
                    evidence.TextHash == TranslationWorkbenchRules.Hash(text);
                var knownEvidence = evidenceMatchesText ? evidence : null;
                var status = Status(locale, field.SourceLocale, field.SourceText, text, sourceHash, knownEvidence);
                targets.Add(new TranslationTargetStatusDto(locale, status, text,
                    EvidenceDto(knownEvidence, text)));
            }

            rows.Add(new TranslationFieldStatusDto(
                field.FieldRef, field.SourceLocale, sourceHash, field.SourceText, targets));
        }

        return new TranslationPreviewDto(rows);
    }

    private static string Status(
        string locale,
        string sourceLocale,
        string sourceText,
        string? targetText,
        string sourceHash,
        TranslationFieldProvenance? evidence)
    {
        if (locale == sourceLocale) return "current";
        if (string.IsNullOrWhiteSpace(targetText)) return "missing";
        if (string.Equals(targetText.Trim(), sourceText.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "sourceCopy";
        }

        if (evidence is not null && evidence.SourceHash != sourceHash &&
            evidence.Kind is "ai" or "template") return "stale";
        if (evidence?.ReviewStatus == "reviewed") return "current";

        return "current";
    }

    private static TranslationProvenanceDto? EvidenceDto(
        TranslationFieldProvenance? evidence,
        string? targetText)
    {
        if (string.IsNullOrWhiteSpace(targetText)) return null;
        return evidence is null
            ? new TranslationProvenanceDto("legacyUnknown", null, null, null, "unknown", null, null)
            : new TranslationProvenanceDto(evidence.Kind, evidence.TemplateId,
                evidence.TemplateRevision, evidence.SourceHash, evidence.ReviewStatus,
                evidence.ReviewerId, evidence.ReviewedAt);
    }
}
