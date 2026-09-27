using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueImportStateStore
{
    Task<CatalogueImportSession> LoadAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<CatalogueImportBatchContext> LoadBatchContextAsync(
        CatalogueImportSession session,
        CancellationToken cancellationToken);
    CatalogueImportResultDto ReadCachedResult(CatalogueImportSession session);
    Task<IReadOnlyList<CatalogueTemplateAdoption>> AddOutcomeMappingsAsync(
        CatalogueImportSession session,
        CatalogueImportSessionTemplate item,
        CatalogueTemplateImportOutcome outcome,
        CatalogueImportBatchContext batchContext,
        CancellationToken cancellationToken);
    Task SaveSkippedAsync(
        CatalogueImportSession session,
        IReadOnlyList<CatalogueImportSessionTemplate> items,
        CancellationToken cancellationToken);
    Task SaveFailedAsync(
        CatalogueImportSession session,
        CatalogueImportSessionTemplate item,
        string failureCode,
        CancellationToken cancellationToken);
    Task<CatalogueImportResultDto> CompleteAsync(Guid sessionId, CancellationToken cancellationToken);
}
