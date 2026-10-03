using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Commands.StartGuestAccountCheckoutCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.CancelGuestAccountCheckoutCommand;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestAccountCheckoutQuery;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments;

[ApiController]
[Route("api/table-guest-visits/{serviceSessionId:guid}/account-payments/operations/{operationId:guid}/checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(TableGuestVisitRateLimitPolicies.RoundPolicyName)]
public sealed class GuestAccountCheckoutController(CustomMediator mediator) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AccountCheckoutStartDto>>> Start(Guid serviceSessionId,
        Guid operationId, [FromBody] StartAccountCheckoutRequest request,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential,
        [FromHeader(Name = AccountReceiptCredentialCrypto.HeaderName)] string? receiptCredential) =>
        Ok(await mediator.SendCommand(new StartGuestAccountCheckoutCommand(serviceSessionId, operationId,
            request, participantCredential, receiptCredential), HttpContext.RequestAborted));

    [HttpPost("cancel")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AccountCheckoutStartDto>>> Cancel(Guid serviceSessionId,
        Guid operationId, [FromBody] StartAccountCheckoutRequest request,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential) =>
        Ok(await mediator.SendCommand(new CancelGuestAccountCheckoutCommand(serviceSessionId, operationId,
            request, participantCredential), HttpContext.RequestAborted));

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AccountCheckoutStartDto>>> Get(Guid serviceSessionId,
        Guid operationId, [FromHeader(Name = "X-Table-Participant")] string? participantCredential) =>
        Ok(await mediator.SendQuery(new GetGuestAccountCheckoutQuery(serviceSessionId, operationId,
            participantCredential), HttpContext.RequestAborted));
}
