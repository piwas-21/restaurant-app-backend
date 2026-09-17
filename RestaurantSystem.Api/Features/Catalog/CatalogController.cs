using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Catalog.Queries.GetCatalogQuery;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Catalog;

[ApiController]
[Route("api/[controller]")]
public sealed class CatalogController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous] // Public by design: this is the guest menu read model and exposes no admin-only fields.
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CatalogOfferFamilyDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<CatalogOfferFamilyDto>>>> GetCatalog(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] OrderType? requestedOrderType = null)
    {
        var result = await mediator.SendQuery(new GetCatalogQuery(page, pageSize, categoryId, requestedOrderType));
        return Ok(result);
    }
}
