using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Commands.CreateCustomerReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
[AllowAnonymous]
[Route("api/customer-mobile/reservations/create")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileReservationCreateController(CustomMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> Create(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationCreateDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new CreateCustomerReservationCommand(sessionId, request), cancellationToken));

    // No module attribute: sealing/recovering an original attempt remains possible after withdrawal.
    [HttpPost("cancel-attempt")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> CancelAttempt(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationCreateDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new CreateCustomerReservationCommand(sessionId, request, true), cancellationToken));
}
