using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Commands;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments;

[ApiController]
[Route("api/customer-mobile/table-payments/checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(TableGuestVisitRateLimitPolicies.AccountPolicyName)]
public sealed class CustomerMobileTableCheckoutController(CustomMediator mediator) : ControllerBase
{
    private const int MaximumBodyBytes = 16 * 1024;

    [HttpPost("start")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTableCheckoutDto>>> Start(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTablePaymentProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new StartCustomerTableCheckoutCommand(sessionId, proof, participantToken), cancellationToken));

    [HttpPost("recover")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTableCheckoutDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTablePaymentProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new RecoverCustomerTableCheckoutCommand(sessionId, proof, participantToken), cancellationToken));

    [HttpPost("stop")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumBodyBytes)]
    public async Task<ActionResult<ApiResponse<CustomerTableCheckoutDto>>> Stop(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantToken,
        [FromBody] CustomerTablePaymentProofDto proof, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new StopCustomerTableCheckoutCommand(sessionId, proof, participantToken), cancellationToken));
}
