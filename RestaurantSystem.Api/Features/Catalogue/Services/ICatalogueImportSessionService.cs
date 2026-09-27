using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueImportSessionService
{
    Task<CatalogueImportSessionDto> CreateAsync(
        CreateCatalogueImportSessionRequest request,
        CancellationToken cancellationToken);

    Task<CatalogueImportSessionDto> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<CatalogueImportSessionDto> UpdateItemsAsync(
        Guid sessionId,
        UpdateCatalogueImportSessionRequest request,
        CancellationToken cancellationToken);

    Task<CatalogueImportPreviewDto> PreviewAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<CatalogueImportResultDto> ImportAsync(
        Guid sessionId,
        ImportCatalogueSessionRequest request,
        CancellationToken cancellationToken);

}
