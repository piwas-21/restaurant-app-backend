using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetActiveTableServiceSessionsQuery;
using RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableServiceSessionQuery;

namespace RestaurantSystem.Api.Features.TableServiceSessions;

/// <summary>
/// Explicit table-visit contract for the POS. Membership is assigned by the idempotent
/// staff-order contract; table number remains only a compatibility key for legacy bill endpoints.
/// </summary>
[ApiController]
[Route("api/table-service-sessions")]
[RequireTableServiceStaff]
public sealed class TableServiceSessionsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public TableServiceSessionsController(CustomMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<TableServiceSessionDto>>>> List()
        => Ok(await _mediator.SendQuery(new GetActiveTableServiceSessionsQuery()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TableServiceSessionDto>>> Open(
        [FromBody] OpenTableServiceSessionCommand command)
        => Ok(await _mediator.SendCommand(command));

    [HttpGet("{serviceSessionId:guid}")]
    public async Task<ActionResult<ApiResponse<TableServiceSessionDto>>> Get(Guid serviceSessionId)
        => Ok(await _mediator.SendQuery(new GetTableServiceSessionQuery(serviceSessionId)));

    [HttpPost("{serviceSessionId:guid}/payments")]
    public async Task<ActionResult<ApiResponse<TableServiceSessionDto>>> Pay(
        Guid serviceSessionId, [FromBody] AddTableServiceSessionPaymentCommand command)
    {
        command.ServiceSessionId = serviceSessionId;
        return Ok(await _mediator.SendCommand(command));
    }

    [HttpPost("{serviceSessionId:guid}/close")]
    public async Task<ActionResult<ApiResponse<TableServiceSessionDto>>> Close(
        Guid serviceSessionId, [FromBody] CloseTableServiceSessionCommand command)
    {
        command.ServiceSessionId = serviceSessionId;
        return Ok(await _mediator.SendCommand(command));
    }
}
