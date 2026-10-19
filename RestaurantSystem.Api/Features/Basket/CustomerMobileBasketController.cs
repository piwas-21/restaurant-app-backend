using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Commands.AddToBasketCommand;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services.Operations;

namespace RestaurantSystem.Api.Features.Basket;

[ApiController]
[Route("api/customer-mobile/basket")]
public sealed class CustomerMobileBasketController(CustomMediator mediator) : ControllerBase
{
    // A dedicated route makes an older deployment refuse rather than ignore an operation ID.
    [HttpPost("items")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<BasketDto>>> AddItem(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] AddToBasketDto request)
    {
        if (request.OperationId is null || request.OperationId == Guid.Empty)
            throw new BadRequestException("A non-empty operation ID is required.");
        return Ok(await mediator.SendCommand(AddToBasketCommand.FromRequest(sessionId, request)));
    }
}
