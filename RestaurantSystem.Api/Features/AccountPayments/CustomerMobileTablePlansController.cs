using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Commands;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Queries;

namespace RestaurantSystem.Api.Features.AccountPayments;

[ApiController]
[Route("api/customer-mobile/table-payments/plans")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(TableGuestVisitRateLimitPolicies.AccountPolicyName)]
public sealed class CustomerMobileTablePlansController(CustomMediator mediator) : ControllerBase
{
    private const int MaximumBodyBytes = 128 * 1024;
    [HttpPost("prepare")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTablePlanPreparedDto>>> Prepare(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTablePlanIntentDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new PrepareCustomerTablePlanQuery(sessionId, participantToken, request), cancellationToken));
    [HttpPost("submit")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTablePlanResolutionDto>>> Submit(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTablePlanSubmissionDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new SubmitCustomerTablePlanCommand(sessionId, participantToken, request), cancellationToken));
    [HttpPost("recover")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTablePlanResolutionDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerTablePlanProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new RecoverCustomerTablePlanQuery(sessionId, proof), cancellationToken));
    [HttpPost("stop")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTablePlanResolutionDto>>> Stop(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CustomerTablePlanProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new StopCustomerTablePlanCommand(sessionId, proof), cancellationToken));
}
