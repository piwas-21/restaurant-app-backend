using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Queries.GetServerFloorSnapshotQuery;

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
}
