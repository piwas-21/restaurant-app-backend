using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.RecoverTableOccupancyCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableOccupancyRecoveryOperationQuery;
using RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableOccupancyRecoveryPreviewQuery;

namespace RestaurantSystem.Api.Features.TableServiceSessions;

[ApiController]
[Route("api/Tables/{tableId:guid}/occupancy-recovery")]
[RequireTableServiceStaff]
[RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
public sealed class TableOccupancyRecoveryController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<TableOccupancyRecoveryPreviewDto>>> GetPreview(
        Guid tableId, [FromQuery] Guid? serviceSessionId) =>
        Ok(await mediator.SendQuery(
            new GetTableOccupancyRecoveryPreviewQuery(tableId, serviceSessionId)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TableOccupancyRecoveryOperationDto>>> Recover(
        Guid tableId, [FromBody] RecoverTableOccupancyCommand command)
    {
        command.TableId = tableId;
        return Ok(await mediator.SendCommand(command));
    }

    [HttpGet("operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<TableOccupancyRecoveryOperationDto>>> GetOperation(
        Guid tableId, Guid operationId) =>
        Ok(await mediator.SendQuery(new GetTableOccupancyRecoveryOperationQuery(tableId, operationId)));
}
