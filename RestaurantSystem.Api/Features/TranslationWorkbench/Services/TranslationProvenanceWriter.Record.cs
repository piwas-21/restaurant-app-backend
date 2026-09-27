using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed partial class TranslationProvenanceWriter
{
    public async Task ClearSourceLocaleAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var tenantSources = await context.TranslationFieldProvenances
            .Where(row => row.EntityType == entityType && row.EntityId == entityId &&
                (row.FieldKey == NameField || row.FieldKey == DescriptionField) &&
                row.Locale == row.SourceLocale &&
                row.Kind == "tenantSource")
            .ToListAsync(cancellationToken);
        context.TranslationFieldProvenances.RemoveRange(tenantSources);
    }

    public async Task RecordAsync(
        string entityType,
        Guid entityId,
        TranslationOwnerMetadataDto? metadata,
        TranslationTextMap text,
        CancellationToken cancellationToken)
    {
        if (metadata is null) return;
        Validate(metadata, text);
        var previous = await context.TranslationFieldProvenances
            .Where(row => row.EntityType == entityType && row.EntityId == entityId)
            .ToListAsync(cancellationToken);
        var state = new RecordContext
        {
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadata,
            Text = text,
            Previous = previous,
            ByKey = previous.ToDictionary(row => (row.FieldKey, row.Locale)),
            Accepted = await LoadAcceptedAsync(metadata, cancellationToken),
            PendingSuggestions = await LoadPendingSuggestionsAsync(entityType, entityId, cancellationToken),
            Actor = currentUser.GetAuditIdentifier()
        };

        foreach (var (fieldKey, sourceLocale) in metadata.SourceLocales)
        {
            RecordField(state, fieldKey, sourceLocale);
        }
    }

    private void RecordField(RecordContext state, string fieldKey, string sourceLocale)
    {
        var sourceText = GetSourceText(state.Text, fieldKey);
        if (string.IsNullOrWhiteSpace(sourceText)) return;
        var sourceHash = TranslationWorkbenchRules.Hash($"{sourceLocale}\n{sourceText}");
        MarkStaleSuggestions(state, fieldKey, sourceHash);
        ReclassifyOldSourceEvidence(state, fieldKey, sourceLocale);
        foreach (var (locale, value) in GetTranslations(state.Text, fieldKey))
        {
            RecordTranslation(state, fieldKey, sourceLocale, sourceHash, locale, value);
        }

        RecordSourceEvidence(state, fieldKey, sourceLocale, sourceHash, sourceText);
    }

    private static void MarkStaleSuggestions(RecordContext state, string fieldKey, string sourceHash)
    {
        foreach (var suggestion in state.PendingSuggestions.Where(row =>
            row.FieldKey == fieldKey && row.SourceHash != sourceHash))
        {
            suggestion.Status = "stale";
            suggestion.UpdatedAt = DateTime.UtcNow;
            suggestion.UpdatedBy = state.Actor;
        }
    }

    private static void ReclassifyOldSourceEvidence(
        RecordContext state,
        string fieldKey,
        string sourceLocale)
    {
        foreach (var oldSource in state.Previous.Where(row => row.FieldKey == fieldKey &&
            row.Locale != sourceLocale && row.Kind == "tenantSource"))
        {
            oldSource.Kind = "manual";
            oldSource.ReviewStatus = ReviewedStatus;
        }
    }

    private void RecordTranslation(
        RecordContext state,
        string fieldKey,
        string sourceLocale,
        string sourceHash,
        string locale,
        string? value)
    {
        if (locale == sourceLocale || !TranslationWorkbenchRules.GuestLocales.Contains(locale)) return;
        state.ByKey.TryGetValue((fieldKey, locale), out var existing);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing is not null) context.TranslationFieldProvenances.Remove(existing);
            return;
        }

        var textHash = TranslationWorkbenchRules.Hash(value);
        state.Metadata.AcceptedSuggestionIds.TryGetValue($"{fieldKey}.{locale}", out var acceptedId);
        var suggestion = acceptedId is null ? null : state.Accepted[Guid.Parse(acceptedId)];
        if (suggestion is not null)
        {
            VerifyAccepted(suggestion, new AcceptedSuggestionExpectation
            {
                EntityType = state.EntityType,
                EntityId = state.EntityId,
                FieldKey = fieldKey,
                Locale = locale,
                SourceHash = sourceHash,
                Text = value,
                Actor = state.Actor
            });
            Upsert(existing, new ProvenanceWrite
            {
                EntityType = state.EntityType,
                EntityId = state.EntityId,
                FieldKey = fieldKey,
                Locale = locale,
                SourceLocale = sourceLocale,
                SourceHash = sourceHash,
                TextHash = textHash,
                Kind = "ai",
                ReviewStatus = ReviewedStatus,
                Actor = state.Actor,
                ContextHash = suggestion.ContextHash
            });
        }
        else if (existing is not null && existing.TextHash != textHash)
        {
            Upsert(existing, new ProvenanceWrite
            {
                EntityType = state.EntityType,
                EntityId = state.EntityId,
                FieldKey = fieldKey,
                Locale = locale,
                SourceLocale = sourceLocale,
                SourceHash = sourceHash,
                TextHash = textHash,
                Kind = "manual",
                ReviewStatus = ReviewedStatus,
                Actor = state.Actor
            });
        }
        // An unchanged old value without provenance remains legacy-unknown.
    }

    private void RecordSourceEvidence(
        RecordContext state,
        string fieldKey,
        string sourceLocale,
        string sourceHash,
        string sourceText)
    {
        state.ByKey.TryGetValue((fieldKey, sourceLocale), out var existing);
        var sourceTextHash = TranslationWorkbenchRules.Hash(sourceText);
        var keepTemplate = existing is not null && existing.Kind == "template" &&
            existing.TextHash == sourceTextHash;
        Upsert(existing, new ProvenanceWrite
        {
            EntityType = state.EntityType,
            EntityId = state.EntityId,
            FieldKey = fieldKey,
            Locale = sourceLocale,
            SourceLocale = sourceLocale,
            SourceHash = sourceHash,
            TextHash = sourceTextHash,
            Kind = keepTemplate ? "template" : "tenantSource",
            ReviewStatus = keepTemplate ? existing!.ReviewStatus : "source",
            TemplateId = keepTemplate ? existing!.TemplateId : null,
            TemplateRevision = keepTemplate ? existing!.TemplateRevision : null,
            Actor = state.Actor
        });
    }

}
