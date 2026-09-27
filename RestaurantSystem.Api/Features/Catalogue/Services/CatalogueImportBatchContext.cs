using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueImportBatchContext
{
    private readonly Guid adoptionId;
    private readonly bool createNewCopy;
    private readonly Dictionary<CatalogueAdoptionSourceKey, List<CatalogueAdoptionLookupRow>> mappings;
    private readonly HashSet<CatalogueLocalEntityKey> existingEntities;
    private bool isFresh = true;

    internal CatalogueImportBatchContext(
        Guid adoptionId,
        bool createNewCopy,
        IEnumerable<CatalogueAdoptionLookupRow> mappings,
        HashSet<CatalogueLocalEntityKey> existingEntities)
    {
        this.adoptionId = adoptionId;
        this.createNewCopy = createNewCopy;
        this.mappings = mappings.GroupBy(SourceKey)
            .ToDictionary(group => group.Key, group => group.ToList());
        this.existingEntities = existingEntities;
    }

    internal CatalogueTemplateAdoption? FindReusableMapping(
        string templateId,
        int revision,
        string entityType)
    {
        if (!mappings.TryGetValue(new(templateId, revision, null, entityType), out var rows)) return null;

        var mapping = rows.FirstOrDefault(row => row.AdoptionId == adoptionId) ??
            (createNewCopy ? null : rows.FirstOrDefault(row => row.IsDefault));
        if (mapping is null) return null;

        return new CatalogueTemplateAdoption
        {
            AdoptionId = mapping.AdoptionId,
            SourceTemplateId = mapping.SourceTemplateId,
            SourceRevision = mapping.SourceRevision,
            SourceEntryId = mapping.SourceEntryId,
            LocalEntityType = mapping.LocalEntityType,
            LocalEntityId = mapping.LocalEntityId,
            ContentHash = mapping.ContentHash,
            IsDefault = mapping.IsDefault,
            CreatedBy = string.Empty
        };
    }

    internal CatalogueTemplateAdoption? FindDependencyMapping(
        string templateId,
        int revision,
        string entityType)
    {
        if (createNewCopy || !mappings.TryGetValue(new(templateId, revision, null, entityType), out var rows))
            return null;

        var mapping = rows.FirstOrDefault(row => row.IsDefault);
        if (mapping is null || !LocalEntityExists(entityType, mapping.LocalEntityId)) return null;

        return new CatalogueTemplateAdoption
        {
            AdoptionId = mapping.AdoptionId,
            SourceTemplateId = mapping.SourceTemplateId,
            SourceRevision = mapping.SourceRevision,
            SourceEntryId = mapping.SourceEntryId,
            LocalEntityType = mapping.LocalEntityType,
            LocalEntityId = mapping.LocalEntityId,
            ContentHash = mapping.ContentHash,
            IsDefault = mapping.IsDefault,
            CreatedBy = string.Empty
        };
    }

    internal bool LocalEntityExists(string? entityType, Guid id) =>
        id != Guid.Empty && existingEntities.Contains(new CatalogueLocalEntityKey(entityType ?? string.Empty, id));

    internal bool TryGetOwnMapping(CatalogueTemplateAdoption candidate, out CatalogueAdoptionLookupRow? mapping) =>
        TryGet(candidate, row => row.AdoptionId == adoptionId, out mapping);

    internal bool HasDefaultMapping(CatalogueTemplateAdoption candidate) =>
        TryGet(candidate, row => row.IsDefault, out _);

    internal void RegisterCommitted(IEnumerable<CatalogueTemplateAdoption> committed)
    {
        foreach (var value in committed)
        {
            var row = new CatalogueAdoptionLookupRow(
                value.AdoptionId, value.SourceTemplateId, value.SourceRevision, value.SourceEntryId,
                value.LocalEntityType, value.LocalEntityId, value.ContentHash, value.IsDefault);
            var key = SourceKey(row);
            if (!mappings.TryGetValue(key, out var rows)) mappings[key] = rows = [];
            if (rows.All(existing => existing.AdoptionId != row.AdoptionId)) rows.Add(row);
            RegisterLocalEntity(row.LocalEntityType, row.LocalEntityId);
        }
    }

    internal void RegisterLocalEntity(string entityType, Guid id)
    {
        if (id != Guid.Empty) existingEntities.Add(new CatalogueLocalEntityKey(entityType, id));
    }

    internal void EnsureFresh()
    {
        if (!isFresh)
        {
            throw new ServiceUnavailableException(
                "Catalogue adoption state could not be refreshed. Retry the failed import items after reloading the session.");
        }
    }

    internal void MarkStale() => isFresh = false;

    private bool TryGet(
        CatalogueTemplateAdoption candidate,
        Func<CatalogueAdoptionLookupRow, bool> predicate,
        out CatalogueAdoptionLookupRow? mapping)
    {
        if (mappings.TryGetValue(SourceKey(candidate), out var rows))
        {
            mapping = rows.FirstOrDefault(predicate);
            return mapping is not null;
        }

        mapping = null;
        return false;
    }

    internal static async Task<CatalogueImportBatchContext> LoadAsync(
        ApplicationDbContext context,
        CatalogueImportSession session,
        CancellationToken cancellationToken)
    {
        var sourceRevisions = session.Templates.Select(item => (item.TemplateId, item.Revision)).Distinct().ToArray();
        var mappingRows = await context.CatalogueTemplateAdoptions.AsNoTracking()
            .Where(mapping => mapping.AdoptionId == session.AdoptionId || mapping.IsDefault)
            .Where(CatalogueSourceRevisionPredicate.ForAdoptions(sourceRevisions))
            .Select(mapping => new CatalogueAdoptionLookupRow(
                mapping.AdoptionId,
                mapping.SourceTemplateId,
                mapping.SourceRevision,
                mapping.SourceEntryId,
                mapping.LocalEntityType,
                mapping.LocalEntityId,
                mapping.ContentHash,
                mapping.IsDefault))
            .ToListAsync(cancellationToken);
        var references = mappingRows.Select(mapping => new CatalogueLocalEntityKey(mapping.LocalEntityType, mapping.LocalEntityId))
            .Concat(session.Templates.Where(item => item.Status == CatalogueImportItemStatus.Imported && item.LocalEntityId.HasValue)
                .Select(item => new CatalogueLocalEntityKey(item.LocalEntityType ?? string.Empty, item.LocalEntityId!.Value)))
            .Concat(ReuseReferences(session.Templates));
        var existing = await CatalogueImportEntityLookup.LoadExistingAsync(context, references, cancellationToken);
        return new CatalogueImportBatchContext(session.AdoptionId, session.CreateNewCopy, mappingRows, existing);
    }

    internal async Task RefreshAsync(
        ApplicationDbContext context,
        CatalogueImportSession session,
        CancellationToken cancellationToken)
    {
        var refreshed = await LoadAsync(context, session, cancellationToken);
        mappings.Clear();
        foreach (var (key, rows) in refreshed.mappings) mappings[key] = rows;
        existingEntities.Clear();
        existingEntities.UnionWith(refreshed.existingEntities);
    }

    private static IEnumerable<CatalogueLocalEntityKey> ReuseReferences(
        IEnumerable<CatalogueImportSessionTemplate> templates)
    {
        foreach (var item in templates)
        {
            if (string.IsNullOrWhiteSpace(item.DecisionJson)) continue;
            using var document = JsonDocument.Parse(item.DecisionJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("resolution", out var resolution) ||
                !string.Equals(resolution.GetString(), "Reuse", StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("localEntityId", out var localId) ||
                !localId.TryGetGuid(out var id)) continue;

            var entityType = CatalogueImportReviewRules.ExpectedEntityType(item.Type);
            if (entityType.Length > 0) yield return new CatalogueLocalEntityKey(entityType, id);
        }
    }

    private static CatalogueAdoptionSourceKey SourceKey(CatalogueTemplateAdoption mapping) =>
        new(mapping.SourceTemplateId, mapping.SourceRevision, mapping.SourceEntryId, mapping.LocalEntityType);

    private static CatalogueAdoptionSourceKey SourceKey(CatalogueAdoptionLookupRow mapping) =>
        new(mapping.SourceTemplateId, mapping.SourceRevision, mapping.SourceEntryId, mapping.LocalEntityType);
}

internal readonly record struct CatalogueAdoptionSourceKey(
    string TemplateId,
    int Revision,
    string? EntryId,
    string EntityType);

internal sealed record CatalogueAdoptionLookupRow(
    Guid AdoptionId,
    string SourceTemplateId,
    int SourceRevision,
    string? SourceEntryId,
    string LocalEntityType,
    Guid LocalEntityId,
    string ContentHash,
    bool IsDefault);
