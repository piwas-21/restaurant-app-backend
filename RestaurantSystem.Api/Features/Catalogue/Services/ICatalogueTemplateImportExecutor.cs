using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueTemplateImportExecutor
{
    Task<CatalogueTemplateImportOutcome> ExecuteAsync(
        CatalogueImportSession session,
        CatalogueImportSessionTemplate item,
        CatalogueImportBatchContext batchContext,
        CancellationToken cancellationToken);
}

public sealed record CatalogueTemplateImportOutcome(
    CatalogueImportItemStatus Status,
    string? LocalEntityType,
    Guid? LocalEntityId,
    IReadOnlyList<CatalogueTemplateEntryMapping> EntryMappings);

public sealed record CatalogueTemplateEntryMapping(
    string SourceTemplateId,
    int SourceRevision,
    string SourceEntryId,
    string LocalEntityType,
    Guid LocalEntityId,
    string ContentHash);
