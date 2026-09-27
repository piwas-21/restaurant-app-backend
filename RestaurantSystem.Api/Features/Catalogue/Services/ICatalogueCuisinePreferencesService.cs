using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueCuisinePreferencesService
{
    Task<CatalogueCuisinePreferencesDto> GetAsync(CancellationToken cancellationToken);

    Task<CatalogueCuisinePreferencesDto> ReplaceAsync(
        IReadOnlyCollection<string> cuisines,
        CancellationToken cancellationToken);
}
