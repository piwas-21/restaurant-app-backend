using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ConfirmReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.DeleteReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.UpdateMyReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.UpdateReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery;
using RestaurantSystem.Api.Features.Reservations.Queries.GetReservationsQuery;
using RestaurantSystem.Domain.Common.Enums;
using System.Security.Claims;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
[RequireModule(ModuleIds.Reservations)]
[Route("api/[controller]")]
public class ReservationsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public ReservationsController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Get all reservations (admin) or the caller's own reservations.</summary>
    [HttpGet]
    [ApiScope(ApiTokenScopes.ReservationsRead)]
    [Authorize]
    public async Task<ActionResult<ApiResponse<PagedResult<ReservationDto>>>> GetReservations(
        [FromQuery] DateTime? date = null,
        [FromQuery] Guid? tableId = null,
        [FromQuery] ReservationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        Guid? customerId = null;
        if (User.FindFirst(ClaimTypes.Role)?.Value != "Admin")
        {
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                return Unauthorized(ApiResponse<PagedResult<ReservationDto>>.Failure("Invalid user ID"));
            }
            customerId = userId;
        }

        var result = await _mediator.SendQuery(
            new GetReservationsQuery(date, tableId, status, customerId, page, pageSize));
        return Ok(result);
    }

    [HttpGet("available-slots")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AvailableTimeSlotsDto>>> GetAvailableTimeSlots(
        [FromQuery] DateTime date,
        [FromQuery] int numberOfGuests)
    {
        if (numberOfGuests <= 0)
        {
            return BadRequest(ApiResponse<AvailableTimeSlotsDto>.Failure("Number of guests must be greater than 0"));
        }

        return Ok(await _mediator.SendQuery(new GetAvailableTimeSlotsQuery(date, numberOfGuests)));
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> CreateReservation([FromBody] CreateReservationDto reservationData)
    {
        Guid? customerId = null;
        if (User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            customerId = userId;
        }

        var result = await _mediator.SendCommand(new CreateReservationCommand(reservationData, customerId));
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{id}")]
    [ApiScope(ApiTokenScopes.ReservationsWrite)]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> UpdateReservation(Guid id, [FromBody] UpdateReservationDto reservationData)
    {
        var result = await _mediator.SendCommand(new UpdateReservationCommand(id, reservationData));
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>The signed-in guest edits their OWN booking — no status, no table, no admin notes.</summary>
    /// <remarks>
    /// Separate from the admin <c>PUT /api/Reservations/{id}</c> on purpose: that route's DTO
    /// REQUIRES <c>Status</c> and <c>TableId</c>, so opening it to customers would let them confirm
    /// their own booking and move themselves onto any table (mobile BACKEND-NOTES item 1). The
    /// caller is resolved from the token inside the handler and is never taken from the request.
    /// </remarks>
    [HttpPut("{id:guid}/mine")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> UpdateMyReservation(
        Guid id, [FromBody] UpdateMyReservationDto reservationData)
    {
        // No Success check: every refusal on this path is an exception carrying its own status and
        // ErrorCode (404 / 400), so a returned response is always the success one.
        return Ok(await _mediator.SendCommand(new UpdateMyReservationCommand(id, reservationData)));
    }

    [HttpPost("{id}/cancel")]
    [ApiScope(ApiTokenScopes.ReservationsWrite)]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> CancelReservation(Guid id)
    {
        // Ownership is enforced inside the handler (EnforceOwnership defaults to true), so the
        // caller cannot be spoofed from the route and the anonymous email link stays the only
        // opt-out. It used to be enforced NOWHERE: any signed-in customer could cancel any booking.
        var result = await _mediator.SendCommand(new CancelReservationCommand(id));
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id}/confirm")]
    [ApiScope(ApiTokenScopes.ReservationsWrite)]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> ConfirmReservation(Guid id)
    {
        var result = await _mediator.SendCommand(new ConfirmReservationCommand(id));
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteReservation(Guid id)
    {
        var result = await _mediator.SendCommand(new DeleteReservationCommand(id));
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
