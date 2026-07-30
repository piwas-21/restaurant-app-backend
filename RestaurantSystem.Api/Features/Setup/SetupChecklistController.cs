using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Setup.Commands.AcknowledgeSetupStepCommand;
using RestaurantSystem.Api.Features.Setup.Commands.SetSetupChecklistDismissedCommand;
using RestaurantSystem.Api.Features.Setup.Dtos;
using RestaurantSystem.Api.Features.Setup.Queries.GetSetupChecklistQuery;

namespace RestaurantSystem.Api.Features.Setup;

/// <summary>
/// The first-run setup checklist a new owner is walked through
/// (SOFRA-ONBOARDING-PLAN O4). Admin-only throughout — it is about running the
/// restaurant, and it reveals which modules the tenant bought.
/// </summary>
/// <remarks>
/// Deliberately NOT <c>[RequireModule]</c>-gated: the checklist itself is core, and
/// gating it would 404 the guidance for a Core-only tenant, who needs it most. The
/// individual STEPS are module-filtered instead, inside the query.
/// <para>
/// Both mutations are idempotent PUTs carrying the desired state rather than POSTs
/// that toggle. A checkbox re-sent after a flaky connection must not undo itself.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/setup-checklist")]
public class SetupChecklistController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public SetupChecklistController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<SetupChecklistDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SetupChecklistDto>>> Get()
    {
        var result = await _mediator.SendQuery(new GetSetupChecklistQuery());
        return Ok(result);
    }

    /// <summary>Mark a step done, or undo it. Refused for a derived step.</summary>
    [HttpPut("steps/{key}")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> AcknowledgeStep(
        string key, [FromBody] SetStepDoneRequest request)
    {
        // Route key wins over anything in the body — the URL is the identifier.
        var result = await _mediator.SendCommand(
            new AcknowledgeSetupStepCommand(key, request.IsDone));
        return Ok(result);
    }

    /// <summary>Hide the checklist, or bring it back.</summary>
    [HttpPut("dismissed")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<bool>>> SetDismissed(
        [FromBody] SetDismissedRequest request)
    {
        var result = await _mediator.SendCommand(
            new SetSetupChecklistDismissedCommand(request.IsDismissed));
        return Ok(result);
    }
}
