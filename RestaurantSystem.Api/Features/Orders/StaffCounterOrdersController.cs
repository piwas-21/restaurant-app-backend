using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CreateStaffRoundCommand;
using RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.ReleaseStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetStaffRoundOperationQuery;

namespace RestaurantSystem.Api.Features.Orders;

/// <summary>Authenticated counter-sale contract. The legacy anonymous Orders POST remains unchanged.</summary>
[ApiController]
[Route("api/staff/orders")]
[Authorize]
[RequireTableServiceStaff]
[RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
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

    [HttpPost("round")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateRound(
        [FromBody] CreateStaffRoundCommand command) =>
        Ok(await _mediator.SendCommand(command));

    [HttpGet("round/operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<StaffRoundOperationLookupDto>>> GetRoundOperation(
        Guid operationId) =>
        Ok(await _mediator.SendQuery(new GetStaffRoundOperationQuery(operationId)));

    [HttpPost("{orderId:guid}/release")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Release(
        Guid orderId, [FromBody] ReleaseStaffCounterOrderCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }
}
