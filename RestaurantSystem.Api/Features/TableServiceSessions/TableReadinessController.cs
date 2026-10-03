using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableReadinessOperationQuery;

namespace RestaurantSystem.Api.Features.TableServiceSessions;

[ApiController]
[Route("api/Tables/{tableId:guid}/ready")]
[RequireTableServiceStaff]
[RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
public sealed class TableReadinessController(CustomMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<TableReadinessOperationDto>>> MarkReady(
        Guid tableId, [FromBody] MarkTableReadyCommand command)
    {
        command.TableId = tableId;
        return Ok(await mediator.SendCommand(command));
    }

    [HttpGet("operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<TableReadinessOperationDto>>> GetOperation(
        Guid tableId, Guid operationId) =>
        Ok(await mediator.SendQuery(new GetTableReadinessOperationQuery(tableId, operationId)));
}
