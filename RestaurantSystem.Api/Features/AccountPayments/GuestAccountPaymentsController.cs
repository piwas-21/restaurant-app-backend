using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Commands.CreateGuestAccountPaymentQuoteCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.CreateGuestEqualSharePlanCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.ReleaseGuestAccountPaymentCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.ReserveGuestAccountPaymentCommand;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestAccountPaymentAccountQuery;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestAccountPaymentOperationQuery;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetGuestEqualSharePlanQuery;

namespace RestaurantSystem.Api.Features.AccountPayments;

/// <summary>Participant-token-only online payment surface for one exact open table visit.</summary>
[ApiController]
[Route("api/table-guest-visits/{serviceSessionId:guid}/account-payments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuestAccountPaymentsController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.AccountPolicyName)]
    public async Task<ActionResult<ApiResponse<AccountPaymentAccountDto>>> GetAccount(
        Guid serviceSessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential)
    {
        SetNoStore();
        return Ok(await mediator.SendQuery(new GetGuestAccountPaymentAccountQuery(
            serviceSessionId, participantCredential), HttpContext.RequestAborted));
    }

    [HttpPost("quotes")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.PaymentPolicyName)]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> CreateQuote(
        Guid serviceSessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential,
        [FromBody] CreateAccountPaymentQuoteRequest request)
    {
        SetNoStore();
        return Ok(await mediator.SendCommand(new CreateGuestAccountPaymentQuoteCommand(
            serviceSessionId, participantCredential, request), HttpContext.RequestAborted));
    }

    [HttpPost("equal-share-plans")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.PaymentPolicyName)]
    public async Task<ActionResult<ApiResponse<AccountEqualSharePlanDto>>> CreateEqualSharePlan(
        Guid serviceSessionId,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential,
        [FromBody] CreateAccountEqualSharePlanRequest request)
    {
        SetNoStore();
        return Ok(await mediator.SendCommand(new CreateGuestEqualSharePlanCommand(
            serviceSessionId, participantCredential, request), HttpContext.RequestAborted));
    }

    [HttpGet("operations/{operationId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.AccountPolicyName)]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> GetOperation(
        Guid serviceSessionId, Guid operationId,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential)
    {
        SetNoStore();
        return Ok(await mediator.SendQuery(new GetGuestAccountPaymentOperationQuery(
            serviceSessionId, operationId, participantCredential), HttpContext.RequestAborted));
    }

    [HttpPost("operations/{operationId:guid}/reserve")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.PaymentPolicyName)]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> Reserve(
        Guid serviceSessionId, Guid operationId,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential,
        [FromBody] ReserveAccountPaymentRequest request)
    {
        SetNoStore();
        return Ok(await mediator.SendCommand(new ReserveGuestAccountPaymentCommand(
            serviceSessionId, operationId, participantCredential, request), HttpContext.RequestAborted));
    }

    [HttpPost("operations/{operationId:guid}/release")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.PaymentPolicyName)]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> Release(
        Guid serviceSessionId, Guid operationId,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential,
        [FromBody] ReleaseAccountPaymentRequest request)
    {
        SetNoStore();
        return Ok(await mediator.SendCommand(new ReleaseGuestAccountPaymentCommand(
            serviceSessionId, operationId, participantCredential, request), HttpContext.RequestAborted));
    }

    [HttpGet("equal-share-plans/operations/{operationId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting(TableGuestVisitRateLimitPolicies.AccountPolicyName)]
    public async Task<ActionResult<ApiResponse<AccountEqualSharePlanDto>>> GetEqualSharePlan(
        Guid serviceSessionId, Guid operationId,
        [FromHeader(Name = "X-Table-Participant")] string? participantCredential)
    {
        SetNoStore();
        return Ok(await mediator.SendQuery(new GetGuestEqualSharePlanQuery(
            serviceSessionId, operationId, participantCredential), HttpContext.RequestAborted));
    }

    private void SetNoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }
}
