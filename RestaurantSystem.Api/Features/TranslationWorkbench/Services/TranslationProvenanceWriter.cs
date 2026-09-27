using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed class TranslationProvenanceWriter(
    ApplicationDbContext context,
    ICurrentUserService currentUser) : ITranslationProvenanceWriter
{
    public async Task<int> RecordTemplateAsync(
        string entityType,
        Guid entityId,
        string templateId,
        int templateRevision,
        string sourceLocale,
        TranslationTextMap savedText,
        IReadOnlyList<TemplateTranslationEvidence> reviewedValues,
        CancellationToken cancellationToken)
    {
        if (entityType is not ("product" or "productIngredient" or "productVariation" or
            "menuSection" or "optionSet") || entityId == Guid.Empty ||
            string.IsNullOrWhiteSpace(templateId) || templateId.Length > 120 ||
            templateRevision < 1 || !TranslationWorkbenchRules.GuestLocales.Contains(sourceLocale) ||
            reviewedValues.Count > 20 || reviewedValues.Any(value =>
                value.FieldKey is not ("name" or "description") ||
                !TranslationWorkbenchRules.GuestLocales.Contains(value.Locale) ||
                string.IsNullOrWhiteSpace(value.Text) ||
                value.Text.Length > TranslationWorkbenchRules.MaxLength(value.FieldKey)) ||
            reviewedValues.Select(value => (value.FieldKey, value.Locale)).Distinct().Count() !=
                reviewedValues.Count)
        {
            throw new BadRequestException("Invalid reviewed template translation evidence");
        }

        var previous = await context.TranslationFieldProvenances
            .Where(row => row.EntityType == entityType && row.EntityId == entityId)
            .ToDictionaryAsync(row => (row.FieldKey, row.Locale), cancellationToken);
        var actor = currentUser.GetAuditIdentifier();
        var stored = 0;
        foreach (var value in reviewedValues)
        {
            var sourceText = value.FieldKey == "name"
                ? savedText.SourceName : savedText.SourceDescription;
            if (string.IsNullOrWhiteSpace(sourceText)) continue;
            var actual = value.Locale == sourceLocale ? sourceText :
                (value.FieldKey == "name" ? savedText.Names : savedText.Descriptions)
                    .GetValueOrDefault(value.Locale);
            if (actual is null || !string.Equals(actual, value.Text, StringComparison.Ordinal)) continue;

            previous.TryGetValue((value.FieldKey, value.Locale), out var existing);
            Upsert(existing, entityType, entityId, value.FieldKey, value.Locale,
                sourceLocale, TranslationWorkbenchRules.Hash($"{sourceLocale}\n{sourceText}"),
                TranslationWorkbenchRules.Hash(actual), "template", "reviewed",
                templateId, templateRevision, actor);
            stored++;
        }

        return stored;
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
        var byKey = previous.ToDictionary(row => (row.FieldKey, row.Locale));
        var accepted = await LoadAcceptedAsync(metadata, cancellationToken);
        var pendingSuggestions = await context.TranslationSuggestions
            .Where(row => row.EntityType == entityType && row.EntityId == entityId &&
                row.Status == "suggested")
            .ToListAsync(cancellationToken);
        var actor = currentUser.GetAuditIdentifier();
        foreach (var (fieldKey, sourceLocale) in metadata.SourceLocales)
        {
            var sourceText = fieldKey == "name" ? text.SourceName : text.SourceDescription;
            if (string.IsNullOrWhiteSpace(sourceText)) continue;
            var sourceHash = TranslationWorkbenchRules.Hash($"{sourceLocale}\n{sourceText}");
            var translations = fieldKey == "name" ? text.Names : text.Descriptions;
            foreach (var suggestion in pendingSuggestions.Where(row =>
                row.FieldKey == fieldKey && row.SourceHash != sourceHash))
            {
                suggestion.Status = "stale";
                suggestion.UpdatedAt = DateTime.UtcNow;
                suggestion.UpdatedBy = actor;
            }

            foreach (var oldSource in previous.Where(row => row.FieldKey == fieldKey &&
                row.Locale != sourceLocale && row.Kind == "tenantSource"))
            {
                oldSource.Kind = "manual";
                oldSource.ReviewStatus = "reviewed";
            }
            foreach (var (locale, value) in translations)
            {
                if (locale == sourceLocale) continue;
                if (!TranslationWorkbenchRules.GuestLocales.Contains(locale)) continue;
                byKey.TryGetValue((fieldKey, locale), out var existing);
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (existing is not null) context.TranslationFieldProvenances.Remove(existing);
                    continue;
                }

                var textHash = TranslationWorkbenchRules.Hash(value);
                metadata.AcceptedSuggestionIds.TryGetValue($"{fieldKey}.{locale}", out var acceptedId);
                var suggestion = acceptedId is null ? null : accepted[Guid.Parse(acceptedId)];
                if (suggestion is not null)
                {
                    VerifyAccepted(suggestion, entityType, entityId, fieldKey, locale,
                        sourceHash, value, actor);
                    Upsert(existing, entityType, entityId, fieldKey, locale, sourceLocale,
                        sourceHash, textHash, "ai", "reviewed", null, null, actor);
                }
                else if (existing is not null && existing.TextHash != textHash)
                {
                    Upsert(existing, entityType, entityId, fieldKey, locale, sourceLocale,
                        sourceHash, textHash, "manual", "reviewed", null, null, actor);
                }
                // An unchanged old value without provenance remains legacy-unknown.
            }

            byKey.TryGetValue((fieldKey, sourceLocale), out var sourceEvidence);
            var sourceTextHash = TranslationWorkbenchRules.Hash(sourceText);
            var keepTemplate = sourceEvidence is not null && sourceEvidence.Kind == "template" &&
                sourceEvidence.TextHash == sourceTextHash;
            Upsert(sourceEvidence, entityType, entityId, fieldKey, sourceLocale, sourceLocale,
                sourceHash, sourceTextHash, keepTemplate ? "template" : "tenantSource",
                keepTemplate ? sourceEvidence!.ReviewStatus : "source",
                keepTemplate ? sourceEvidence!.TemplateId : null,
                keepTemplate ? sourceEvidence!.TemplateRevision : null,
                actor);
        }
    }

    private async Task<Dictionary<Guid, TranslationSuggestion>> LoadAcceptedAsync(
        TranslationOwnerMetadataDto metadata,
        CancellationToken cancellationToken)
    {
        var ids = metadata.AcceptedSuggestionIds.Values.Select(Guid.Parse).ToArray();
        var found = await context.TranslationSuggestions
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        if (found.Count != ids.Distinct().Count())
        {
            throw new ConflictException("An accepted translation suggestion no longer exists");
        }

        return found;
    }

    private static void Validate(TranslationOwnerMetadataDto metadata, TranslationTextMap text)
    {
        if (metadata.SourceLocales is null || metadata.AcceptedSuggestionIds is null ||
            metadata.SourceLocales.Count > 2 || metadata.AcceptedSuggestionIds.Count > 20 ||
            metadata.SourceLocales.Any(pair => pair.Key is not ("name" or "description") ||
                !TranslationWorkbenchRules.GuestLocales.Contains(pair.Value)) ||
            metadata.AcceptedSuggestionIds.Any(pair =>
                pair.Key.Split('.') is not ["name" or "description", _] ||
                !TranslationWorkbenchRules.GuestLocales.Contains(pair.Key.Split('.')[1]) ||
                !metadata.SourceLocales.ContainsKey(pair.Key.Split('.')[0]) ||
                !Guid.TryParse(pair.Value, out _)) ||
            text.SourceName.Length > TranslationWorkbenchRules.MaxLength("name") ||
            text.SourceDescription?.Length > TranslationWorkbenchRules.MaxLength("description"))
        {
            throw new BadRequestException("Invalid translation provenance metadata");
        }
    }

    private static void VerifyAccepted(
        TranslationSuggestion suggestion,
        string entityType,
        Guid entityId,
        string fieldKey,
        string locale,
        string sourceHash,
        string text,
        string actor)
    {
        if (suggestion.Status is not ("accepted" or "edited") ||
            suggestion.EntityType != entityType ||
            suggestion.EntityId is Guid existingId && existingId != entityId ||
            suggestion.EntityId is null && suggestion.RequestedBy != actor ||
            suggestion.FieldKey != fieldKey || suggestion.Locale != locale ||
            suggestion.SourceHash != sourceHash || suggestion.ReviewedText != text)
        {
            throw new ConflictException("Accepted translation no longer matches the source or saved text");
        }
    }

    private void Upsert(
        TranslationFieldProvenance? existing,
        string entityType,
        Guid entityId,
        string fieldKey,
        string locale,
        string sourceLocale,
        string sourceHash,
        string textHash,
        string kind,
        string reviewStatus,
        string? templateId,
        int? templateRevision,
        string actor)
    {
        var row = existing ?? new TranslationFieldProvenance
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            FieldKey = fieldKey,
            Locale = locale,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actor
        };
        row.SourceLocale = sourceLocale;
        row.SourceHash = sourceHash;
        row.TextHash = textHash;
        row.Kind = kind;
        row.ReviewStatus = reviewStatus;
        row.TemplateId = templateId;
        row.TemplateRevision = templateRevision;
        row.ReviewerId = actor;
        row.ReviewedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;
        if (existing is null) context.TranslationFieldProvenances.Add(row);
    }
}
