using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueImportPreviewService
{
    Task<CatalogueImportPreviewDto> PreviewAsync(Guid sessionId, CancellationToken cancellationToken);
}
