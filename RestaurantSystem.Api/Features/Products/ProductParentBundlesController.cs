using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Queries.GetProductParentBundlesQuery;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Products;

[ApiController]
[Route("api/Products")]
public sealed class ProductParentBundlesController(CustomMediator mediator) : ControllerBase
{
    [HttpGet("{id:guid}/parent-bundles")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<ProductParentBundlesDto>>> GetParentBundles(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendQuery(
            new GetProductParentBundlesQuery(id), cancellationToken);
        return Ok(result);
    }
}
