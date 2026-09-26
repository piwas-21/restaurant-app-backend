using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos.Requests;
using RestaurantSystem.Api.Features.Products.Queries.QuoteProductQuery;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products;

[ApiController]
[Route("api/Products")]
public sealed class ProductQuoteController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("{id:guid}/quote")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ProductQuoteDto>>> Quote(
        Guid id,
        [FromBody] ProductQuoteRequestDto request,
        [FromQuery] OrderType? requestedOrderType,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(
            new QuoteProductQuery(id, request, requestedOrderType), cancellationToken));
}
