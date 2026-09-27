using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed partial class TranslationProvenanceWriter
{
    private async Task<List<TranslationSuggestion>> LoadPendingSuggestionsAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken) => await context.TranslationSuggestions
        .Where(row => row.EntityType == entityType && row.EntityId == entityId &&
            row.Status == "suggested")
        .ToListAsync(cancellationToken);

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
            metadata.SourceLocales.Any(pair => pair.Key is not (NameField or DescriptionField) ||
                !TranslationWorkbenchRules.GuestLocales.Contains(pair.Value)) ||
            metadata.AcceptedSuggestionIds.Any(pair => !IsValidAcceptedSuggestionKey(metadata, pair)) ||
            text.SourceName.Length > TranslationWorkbenchRules.MaxLength(NameField) ||
            text.SourceDescription?.Length > TranslationWorkbenchRules.MaxLength(DescriptionField))
        {
            throw new BadRequestException("Invalid translation provenance metadata");
        }
    }

    private static bool IsValidAcceptedSuggestionKey(
        TranslationOwnerMetadataDto metadata,
        KeyValuePair<string, string> pair)
    {
        var parts = pair.Key.Split('.');
        return parts.Length == 2 && (parts[0] is NameField or DescriptionField) &&
            TranslationWorkbenchRules.GuestLocales.Contains(parts[1]) &&
            metadata.SourceLocales.ContainsKey(parts[0]) && Guid.TryParse(pair.Value, out _);
    }

    private static void VerifyAccepted(
        TranslationSuggestion suggestion,
        AcceptedSuggestionExpectation expected)
    {
        if (!IsAcceptedStatus(suggestion.Status) || !MatchesAcceptedOwner(suggestion, expected) ||
            suggestion.FieldKey != expected.FieldKey || suggestion.Locale != expected.Locale ||
            suggestion.SourceHash != expected.SourceHash || suggestion.ReviewedText != expected.Text)
        {
            throw new ConflictException("Accepted translation no longer matches the source or saved text");
        }
    }

    private static bool IsAcceptedStatus(string status) => status is "accepted" or "edited";

    private static bool MatchesAcceptedOwner(
        TranslationSuggestion suggestion,
        AcceptedSuggestionExpectation expected) =>
        suggestion.EntityType == expected.EntityType &&
        (suggestion.EntityId is null || suggestion.EntityId == expected.EntityId) &&
        (suggestion.EntityId is not null || suggestion.RequestedBy == expected.Actor);

    private void Upsert(TranslationFieldProvenance? existing, ProvenanceWrite write)
    {
        var row = existing ?? new TranslationFieldProvenance
        {
            Id = Guid.NewGuid(),
            EntityType = write.EntityType,
            EntityId = write.EntityId,
            FieldKey = write.FieldKey,
            Locale = write.Locale,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = write.Actor
        };
        row.SourceLocale = write.SourceLocale;
        row.SourceHash = write.SourceHash;
        row.ContextHash = write.ContextHash;
        row.TextHash = write.TextHash;
        row.Kind = write.Kind;
        row.ReviewStatus = write.ReviewStatus;
        row.TemplateId = write.TemplateId;
        row.TemplateRevision = write.TemplateRevision;
        row.ReviewerId = write.Actor;
        row.ReviewedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = write.Actor;
        if (existing is null) context.TranslationFieldProvenances.Add(row);
    }

    private static string? GetSourceText(TranslationTextMap text, string fieldKey) =>
        fieldKey == NameField ? text.SourceName : text.SourceDescription;

    private static IReadOnlyDictionary<string, string?> GetTranslations(
        TranslationTextMap text,
        string fieldKey) => fieldKey == NameField ? text.Names : text.Descriptions;

    private sealed class RecordContext
    {
        public required string EntityType { get; init; }
        public required Guid EntityId { get; init; }
        public required TranslationOwnerMetadataDto Metadata { get; init; }
        public required TranslationTextMap Text { get; init; }
        public required IReadOnlyList<TranslationFieldProvenance> Previous { get; init; }
        public required Dictionary<(string FieldKey, string Locale), TranslationFieldProvenance> ByKey { get; init; }
        public required Dictionary<Guid, TranslationSuggestion> Accepted { get; init; }
        public required List<TranslationSuggestion> PendingSuggestions { get; init; }
        public required string Actor { get; init; }
    }

    private sealed class ProvenanceWrite
    {
        public required string EntityType { get; init; }
        public required Guid EntityId { get; init; }
        public required string FieldKey { get; init; }
        public required string Locale { get; init; }
        public required string SourceLocale { get; init; }
        public required string SourceHash { get; init; }
        public required string TextHash { get; init; }
        public required string Kind { get; init; }
        public required string ReviewStatus { get; init; }
        public string? TemplateId { get; init; }
        public int? TemplateRevision { get; init; }
        public required string Actor { get; init; }
        public string? ContextHash { get; init; }
    }

    private sealed class AcceptedSuggestionExpectation
    {
        public required string EntityType { get; init; }
        public required Guid EntityId { get; init; }
        public required string FieldKey { get; init; }
        public required string Locale { get; init; }
        public required string SourceHash { get; init; }
        public required string Text { get; init; }
        public required string Actor { get; init; }
    }
}
