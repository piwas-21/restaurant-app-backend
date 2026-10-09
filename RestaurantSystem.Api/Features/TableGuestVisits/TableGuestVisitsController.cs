using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.TableGuestVisits.Commands;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Queries;

namespace RestaurantSystem.Api.Features.TableGuestVisits;

[ApiController]
[Route("api/table-guest-visits")]
public sealed class TableGuestVisitsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public TableGuestVisitsController(CustomMediator mediator) => _mediator = mediator;

    [HttpPost("join")]
    [AllowAnonymous]
    [EnableRateLimiting("table-guest-join")]
    public async Task<ActionResult<ApiResponse<TableGuestJoinDto>>> Join(
        [FromBody] JoinTableGuestVisitRequest request)
    {
        SetNoStore();
        return Ok(await _mediator.SendCommand(
            new JoinTableGuestVisitCommand(request.QrCodeData, request.AdmissionCode), HttpContext.RequestAborted));
    }

    [HttpPost("{serviceSessionId:guid}/admission-code")]
    [RequireTableServiceStaff]
    [RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
    public async Task<ActionResult<ApiResponse<TableGuestAdmissionCodeDto>>> CreateAdmissionCode(
        Guid serviceSessionId,
        [FromQuery] bool preferShortCode = false)
    {
        SetNoStore();
        return Ok(await _mediator.SendCommand(
            new CreateTableGuestAdmissionCodeCommand(serviceSessionId, preferShortCode), HttpContext.RequestAborted));
    }

    [HttpGet("{serviceSessionId:guid}/account")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.AccountPolicyName)]
    public async Task<ActionResult<ApiResponse<TableGuestAccountDto>>> GetAccount(
        Guid serviceSessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken)
    {
        SetNoStore();
        return Ok(await _mediator.SendQuery(
            new GetTableGuestAccountQuery(serviceSessionId, participantToken), HttpContext.RequestAborted));
    }

    [HttpPost("{serviceSessionId:guid}/rounds")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.RoundPolicyName)]
    public async Task<ActionResult<ApiResponse<TableGuestAccountDto>>> CreateRound(
        Guid serviceSessionId,
        [FromHeader(Name = "X-Session-Id")] string? basketSessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CreateGuestRoundRequest request)
    {
        SetNoStore();
        var command = new CreateTableGuestRoundCommand
        {
            ServiceSessionId = serviceSessionId,
            OperationId = request.OperationId,
            ExpectedAccountRevision = request.ExpectedAccountRevision,
            ExpectedBasketFingerprint = request.ExpectedBasketFingerprint,
            BasketSessionId = basketSessionId ?? string.Empty,
            ParticipantToken = participantToken,
        };
        return Ok(await _mediator.SendCommand(command, HttpContext.RequestAborted));
    }

    private void SetNoStore()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Pragma"] = "no-cache";
    }
}
