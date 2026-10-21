using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationCancellationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.SubmitCustomerReservationCancellationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationCancellationCommand;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
[RequireRole(UserRole.Customer)]
[Route("api/customer-mobile/reservations/cancellation")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileReservationCancellationController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("prepare")]
    public async Task<ActionResult<ApiResponse<CustomerReservationCancellationPreparedDto>>> Prepare(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationCancellationDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new PrepareCustomerReservationCancellationCommand(sessionId, request), cancellationToken));

    [HttpPost("submit")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> Submit(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new SubmitCustomerReservationCancellationCommand(sessionId, proof), cancellationToken));

    // Original recovery and attempt stopping remain available after module/native withdrawal.
    [HttpPost("recover")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new ResolveCustomerReservationCancellationCommand(sessionId, proof, false), cancellationToken));

    [HttpPost("cancel-attempt")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> CancelAttempt(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new ResolveCustomerReservationCancellationCommand(sessionId, proof, true), cancellationToken));
}
