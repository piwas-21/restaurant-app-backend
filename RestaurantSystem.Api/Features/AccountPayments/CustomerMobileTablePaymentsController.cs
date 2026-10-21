using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Queries;

namespace RestaurantSystem.Api.Features.AccountPayments;

[ApiController]
[Route("api/customer-mobile/table-payments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(TableGuestVisitRateLimitPolicies.AccountPolicyName)]
public sealed class CustomerMobileTablePaymentsController(CustomMediator mediator) : ControllerBase
{
    private const int MaximumPreparationBodyBytes = 128 * 1024;

    [HttpGet("account")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<CustomerTablePaymentAccountDto>>> Account(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromQuery] int? schemaVersion, [FromQuery] Guid tenantId, [FromQuery] Guid serviceSessionId,
        CancellationToken cancellationToken) => Ok(await mediator.SendQuery(
            new GetCustomerTablePaymentAccountQuery(sessionId, participantToken, schemaVersion, tenantId, serviceSessionId),
            cancellationToken));
    [HttpPost("prepare")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumPreparationBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTablePaymentPreparedDto>>> Prepare(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTablePaymentIntentDto request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new PrepareCustomerTablePaymentQuery(sessionId, participantToken, request), cancellationToken));
}
