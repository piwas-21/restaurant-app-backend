using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueSessionImporter
{
    Task<CatalogueImportResultDto> ImportAsync(
        Guid sessionId,
        ImportCatalogueSessionRequest request,
        CancellationToken cancellationToken);
}
