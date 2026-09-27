using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed class CatalogueRevisionChangesReader(
    ApplicationDbContext context,
    ICentralCatalogueClient catalogue)
{
    private readonly CataloguePublishedRevisionLoader revisionLoader = new(catalogue);
    private readonly CatalogueRevisionLocalTextReader localText = new(context);

    public async Task<CatalogueRevisionChangesDto> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await context.CatalogueImportSessions.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == sessionId, cancellationToken)
            ?? throw new NotFoundException("Catalogue import session was not found");
        var mappings = await LatestMappingsAsync(session.AdoptionId, cancellationToken);
        var supportedMappings = mappings.Where(mapping =>
                CatalogueRevisionTemplateTypes.ForEntity(mapping.LocalEntityType) is not null)
            .ToArray();
        var templateIds = supportedMappings.Select(mapping => mapping.SourceTemplateId)
            .ToHashSet(StringComparer.Ordinal);
        var entryMappings = await LatestEntryMappingsAsync(session.AdoptionId, templateIds, cancellationToken);
        var requests = supportedMappings.Select(mapping =>
            new CatalogueCurrentRevisionRequest(mapping.SourceTemplateId, mapping.SourceRevision)).ToArray();
        var publishedByTemplate = requests.Length == 0
            ? new Dictionary<string, CataloguePublishedRevisionResult>(StringComparer.Ordinal)
            : await revisionLoader.LoadBatchAsync(requests, cancellationToken);
        var localByAdoption = await localText.ReadManyAsync(mappings, entryMappings, cancellationToken);
        var results = mappings.Select(mapping => BuildItem(
            mapping,
            publishedByTemplate.GetValueOrDefault(mapping.SourceTemplateId),
            localByAdoption.GetValueOrDefault(mapping.Id))).ToArray();

        return new CatalogueRevisionChangesDto(sessionId, session.Version, results);
    }

    private async Task<List<CatalogueTemplateAdoption>> LatestMappingsAsync(
        Guid adoptionId,
        CancellationToken cancellationToken)
    {
        var all = await context.CatalogueTemplateAdoptions.AsNoTracking()
            .Where(value => value.AdoptionId == adoptionId && value.SourceEntryId == null)
            .OrderBy(value => value.SourceRevision)
            .ThenBy(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        return all.GroupBy(value => value.SourceTemplateId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(value => value.SourceTemplateId, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<CatalogueTemplateAdoption>> LatestEntryMappingsAsync(
        Guid adoptionId,
        HashSet<string> templateIds,
        CancellationToken cancellationToken)
    {
        if (templateIds.Count == 0)
        {
            return [];
        }

        var mappings = await context.CatalogueTemplateAdoptions.AsNoTracking()
            .Where(value => value.AdoptionId == adoptionId && templateIds.Contains(value.SourceTemplateId) &&
                value.SourceEntryId != null)
            .OrderBy(value => value.SourceRevision)
            .ThenBy(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        return mappings.GroupBy(value => (value.SourceTemplateId, value.SourceEntryId, value.LocalEntityType))
            .Select(group => group.Last())
            .ToList();
    }

    private static CatalogueRevisionChangeItemDto BuildItem(
        CatalogueTemplateAdoption adoption,
        CataloguePublishedRevisionResult? published,
        Dictionary<string, string?>? local)
    {
        var type = CatalogueRevisionTemplateTypes.ForEntity(adoption.LocalEntityType);
        if (type is null)
        {
            return Result(adoption, null, null, false, null, "UnsupportedLocalRecord", [], null,
                "This tenant record type does not support selected text updates.");
        }

        if (published is null)
        {
            return Result(adoption, null, null, false, null, "Unavailable", [], null,
                "Catalogue status is temporarily unavailable; the tenant record was not changed.");
        }

        if (published.Status != "Available" || published.Metadata is null || published.Revision is null)
        {
            return Result(adoption, published.Metadata?.Revision, published.Metadata?.ContentHash,
                published.Status is "Withdrawn" or "AdoptedRevisionWithdrawn",
                published.Metadata?.AdoptedRevisionWithdrawn, published.Status, [], null, published.Notice);
        }

        if (published.Revision.Type != type)
        {
            return Result(adoption, published.Revision.Revision, published.Revision.ContentHash, false,
                published.Metadata.AdoptedRevisionWithdrawn, "Unavailable", [], null,
                "Catalogue returned a revision with a different template type; the tenant record was not changed.");
        }

        if (local is null || local.Count == 0)
        {
            return Result(adoption, published.Revision.Revision, published.Revision.ContentHash, false,
                published.Metadata.AdoptedRevisionWithdrawn, "LocalRecordMissing", [], null,
                "The mapped tenant record or its text fields are no longer available.");
        }

        var baseline = CatalogueRevisionBaseline.Read(adoption.BaselineFieldsJson, type,
            adoption.SourceRevision, adoption.ContentHash);
        var sourceFields = CatalogueRevisionBaseline.Fields(published.Revision, type);
        var candidatePaths = baseline.Keys.Union(sourceFields.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var unmappedSections = candidatePaths.Select(SectionKey)
            .Where(key => key is not null)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !sourceFields.ContainsKey($"sections[{key}].name") ||
                !local.ContainsKey($"sections[{key}].name"))
            .ToHashSet(StringComparer.Ordinal);
        var fields = candidatePaths
            .Where(path => SectionKey(path) is not { } key || !unmappedSections.Contains(key))
            .Select(path =>
            {
                baseline.TryGetValue(path, out var prior);
                sourceFields.TryGetValue(path, out var current);
                local.TryGetValue(path, out var localValue);
                return new CatalogueRevisionFieldChangeDto(path, prior?.Value, current, localValue,
                    !string.Equals(prior?.Value, localValue, StringComparison.Ordinal));
            }).ToArray();
        var hasTextChanges = fields.Any(field => !string.Equals(field.Baseline, field.Current, StringComparison.Ordinal));
        var hasRevisionChanges = adoption.SourceRevision != published.Revision.Revision ||
            !string.Equals(adoption.ContentHash, published.Revision.ContentHash, StringComparison.Ordinal);
        var status = "Current";
        if (hasTextChanges || hasRevisionChanges) status = "UpdateAvailable";
        if (published.Metadata.AdoptedRevisionWithdrawn == true) status = "AdoptedRevisionWithdrawn";
        var notice = status switch
        {
            "AdoptedRevisionWithdrawn" => "The adopted revision is withdrawn. The newer revision is only a suggestion; local data is unchanged.",
            "UpdateAvailable" when !hasTextChanges => "A newer source revision is available, but no supported text fields differ. Operational fields remain tenant-owned.",
            "UpdateAvailable" => "A newer revision is available. Select changed text fields to apply; tenant operational fields remain unchanged.",
            _ => null
        };
        if (unmappedSections.Count > 0)
        {
            var mappingNotice = "Some bundle sections no longer have a tenant mapping; their text fields were omitted and bundle structure was left unchanged.";
            notice = string.IsNullOrWhiteSpace(notice) ? mappingNotice : $"{notice} {mappingNotice}";
        }

        return Result(adoption, published.Revision.Revision, published.Revision.ContentHash, false,
            published.Metadata.AdoptedRevisionWithdrawn, status, fields,
            CatalogueRevisionBaseline.ComputeLocalHash(local), notice);
    }

    private static string? SectionKey(string path)
    {
        const string prefix = "sections[";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var close = path.IndexOf(']', prefix.Length);
        return close > prefix.Length ? path[prefix.Length..close] : null;
    }

    private static CatalogueRevisionChangeItemDto Result(
        CatalogueTemplateAdoption adoption,
        int? currentRevision,
        string? currentHash,
        bool withdrawn,
        bool? adoptedWithdrawn,
        string status,
        IReadOnlyList<CatalogueRevisionFieldChangeDto> fields,
        string? localHash,
        string? notice) => new(
            adoption.SourceTemplateId,
            adoption.SourceRevision,
            adoption.ContentHash,
            currentRevision,
            currentHash,
            withdrawn,
            adoptedWithdrawn,
            status,
            fields,
            notice,
            localHash);
}
