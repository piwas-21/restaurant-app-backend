using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueRejectedCandidateService
{
    Task SyncAsync(
        CatalogueImportSessionTemplate item,
        string locale,
        CatalogueImportItemDecision decision,
        CancellationToken cancellationToken);
}
