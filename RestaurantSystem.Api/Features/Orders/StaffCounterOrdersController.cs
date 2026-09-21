using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.ReleaseStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

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
    private readonly IOrderRoutingService _routing;

    public StaffCounterOrdersController(CustomMediator mediator, IOrderRoutingService routing)
    {
        _mediator = mediator;
        _routing = routing;
    }

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

    /// <summary>Returns the authoritative route lifecycle for a staff-visible order.</summary>
    [HttpGet("{orderId:guid}/routing")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderRoutingStateDto>>>> Routing(
        Guid orderId, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<OrderRoutingStateDto>>.SuccessWithData(
            await _routing.ProjectAsync(orderId, cancellationToken), "Routing state loaded."));
}
