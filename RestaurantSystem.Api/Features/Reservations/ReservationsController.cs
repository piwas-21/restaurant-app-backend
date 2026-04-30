using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.ConfirmReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.DeleteReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Commands.UpdateReservationCommand;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery;
using RestaurantSystem.Api.Features.Reservations.Queries.GetReservationsQuery;
using RestaurantSystem.Domain.Common.Enums;
using System.Security.Claims;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
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
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> UpdateReservation(Guid id, [FromBody] UpdateReservationDto reservationData)
    {
        var result = await _mediator.SendCommand(new UpdateReservationCommand(id, reservationData));
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id}/cancel")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> CancelReservation(Guid id)
    {
        // TODO: enforce non-admins can only cancel their own reservations.
        var result = await _mediator.SendCommand(new CancelReservationCommand(id));
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id}/confirm")]
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
