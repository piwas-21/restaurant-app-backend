using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Commands.DeliverServerTaskCommand;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Queries.GetServerFloorSnapshotQuery;
using RestaurantSystem.Api.Features.ServerWorkspace.Queries.GetServerTasksQuery;

namespace RestaurantSystem.Api.Features.ServerWorkspace;

[ApiController]
[Route("api/staff/server-workspace")]
[RequireTableServiceStaff]
[RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
public sealed class ServerWorkspaceController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public ServerWorkspaceController(CustomMediator mediator) => _mediator = mediator;

    [HttpGet("floor")]
    public async Task<ActionResult<ApiResponse<ServerFloorSnapshotDto>>> GetFloor() =>
        Ok(await _mediator.SendQuery(new GetServerFloorSnapshotQuery()));

    [HttpGet("tasks")]
    public async Task<ActionResult<ApiResponse<ServerTaskFeedDto>>> GetTasks(
        [FromQuery] GetServerTasksQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _mediator.SendQuery(query, cancellationToken));

    [HttpPost("tasks/{orderId:guid}/deliver")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Deliver(
        Guid orderId,
        [FromBody] DeliverServerTaskRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _mediator.SendCommand(
            new DeliverServerTaskCommand(orderId, request.ExpectedVersion), cancellationToken));
}

public sealed record DeliverServerTaskRequest(int ExpectedVersion);
