using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueTemplateGraphLoader
{
    Task<CatalogueTemplateGraph> LoadAsync(
        string rootTemplateId,
        int rootRevision,
        CancellationToken cancellationToken);
}
