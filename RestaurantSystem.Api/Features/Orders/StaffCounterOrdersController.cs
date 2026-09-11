using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.ReleaseStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders;

/// <summary>Authenticated counter-sale contract. The legacy anonymous Orders POST remains unchanged.</summary>
[ApiController]
[Route("api/staff/orders")]
[Authorize]
[RequireStaff]
public sealed class StaffCounterOrdersController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public StaffCounterOrdersController(CustomMediator mediator) => _mediator = mediator;

    [HttpPost("quote")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Quote(
        [FromBody] QuoteStaffCounterOrderCommand command) =>
        Ok(await _mediator.SendCommand(command));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Create(
        [FromBody] CreateStaffCounterOrderCommand command) =>
        Ok(await _mediator.SendCommand(command));

    [HttpPost("{orderId:guid}/release")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Release(
        Guid orderId, [FromBody] ReleaseStaffCounterOrderCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }
}
