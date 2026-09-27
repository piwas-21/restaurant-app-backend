using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.Api.Features.Catalogue;

[ApiController]
[Route("api/catalogue/preferences")]
public sealed class CataloguePreferencesController(ICatalogueCuisinePreferencesService preferences) : ControllerBase
{
    [HttpGet]
    [RequireAdmin]
    [ProducesResponseType(typeof(CatalogueCuisinePreferencesDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogueCuisinePreferencesDto>> Get(CancellationToken cancellationToken) =>
        Ok(await preferences.GetAsync(cancellationToken));

    [HttpPut]
    [RequireAdmin]
    [ProducesResponseType(typeof(CatalogueCuisinePreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CatalogueCuisinePreferencesDto>> Replace(
        [FromBody] CatalogueCuisinePreferencesDto request,
        CancellationToken cancellationToken) =>
        Ok(await preferences.ReplaceAsync(request.Cuisines, cancellationToken));
}
