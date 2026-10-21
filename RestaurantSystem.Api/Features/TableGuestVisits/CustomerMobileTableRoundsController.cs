using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Commands;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Queries;

namespace RestaurantSystem.Api.Features.TableGuestVisits;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/customer-mobile/table-rounds")]
public sealed class CustomerMobileTableRoundsController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("prepare")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.RoundPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerTableRoundPreparedDto>>> Prepare(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTableRoundIntentDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new PrepareCustomerTableRoundQuery(sessionId, participantToken, request), cancellationToken));

    [HttpPost("submit")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.RoundPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerTableRoundResolutionDto>>> Submit(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTableRoundIntentDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new SubmitCustomerTableRoundCommand(sessionId, participantToken, request), cancellationToken));

    [HttpPost("recover")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.RoundPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerTableRoundResolutionDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerTableRoundProofDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new RecoverCustomerTableRoundQuery(sessionId, request), cancellationToken));

    [HttpPost("cancel-attempt")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.RoundPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerTableRoundResolutionDto>>> Stop(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerTableRoundProofDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new StopCustomerTableRoundCommand(sessionId, request), cancellationToken));
}
