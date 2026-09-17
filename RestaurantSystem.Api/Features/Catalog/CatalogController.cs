using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Catalog.Queries.GetCatalogQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Catalog;

[ApiController]
[Route("api/[controller]")]
public sealed class CatalogController(
    CustomMediator mediator,
    IOptions<CatalogSettings> catalogSettings) : ControllerBase
{
    [HttpGet]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous] // Public by design: this is the guest menu read model and exposes no admin-only fields.
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CatalogOfferFamilyDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<CatalogOfferFamilyDto>>>> GetCatalog(
        [FromQuery] int page = 1,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] OrderType? requestedOrderType = null)
    {
        var requestedPageSize = pageSize ?? catalogSettings.Value.MaxPageSize;
        var result = await mediator.SendQuery(
            new GetCatalogQuery(page, requestedPageSize, categoryId, requestedOrderType));
        return Ok(result);
    }
}
