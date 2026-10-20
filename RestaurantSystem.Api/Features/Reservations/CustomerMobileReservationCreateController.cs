using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Commands.CreateCustomerReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationCommand;

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

    [HttpPost("prepare")]
    public async Task<ActionResult<ApiResponse<CustomerReservationProofDto>>> Prepare(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationCreateDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new PrepareCustomerReservationCommand(sessionId, request), cancellationToken));

    // Recovery never reconstructs a booking and remains possible after module/native withdrawal.
    [HttpPost("recover")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new ResolveCustomerReservationCommand(sessionId, proof, false), cancellationToken));

    [HttpPost("cancel-attempt")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> CancelAttempt(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new ResolveCustomerReservationCommand(sessionId, proof, true), cancellationToken));
}
