using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationEditCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.SubmitCustomerReservationEditCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationEditCommand;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
[RequireRole(UserRole.Customer)]
[Route("api/customer-mobile/reservations/edit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileReservationEditController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("prepare")]
    public async Task<ActionResult<ApiResponse<CustomerReservationEditPreparedDto>>> Prepare(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationEditDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new PrepareCustomerReservationEditCommand(sessionId, request), cancellationToken));

    [HttpPost("submit")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> Submit(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationEditDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new SubmitCustomerReservationEditCommand(sessionId, request), cancellationToken));

    // Original proof only: recovery/stop survives native module withdrawal without applying an edit.
    [HttpPost("recover")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new ResolveCustomerReservationEditCommand(sessionId, proof, false), cancellationToken));

    [HttpPost("cancel-attempt")]
    public async Task<ActionResult<ApiResponse<CustomerReservationResolutionDto>>> CancelAttempt(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerReservationProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new ResolveCustomerReservationEditCommand(sessionId, proof, true), cancellationToken));
}
