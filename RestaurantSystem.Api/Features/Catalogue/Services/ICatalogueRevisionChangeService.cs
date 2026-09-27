using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueRevisionChangeService
{
    Task<CatalogueRevisionChangesDto> GetAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<CatalogueRevisionFieldApplyResultDto> ApplyFieldsAsync(
        Guid sessionId,
        ApplyCatalogueRevisionFieldsRequest request,
        CancellationToken cancellationToken);
}
