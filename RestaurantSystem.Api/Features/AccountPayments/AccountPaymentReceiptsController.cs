using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountPaymentReceiptQuery;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments;

[ApiController]
[AllowAnonymous]
[Route("api/account-payment-receipts/{attemptId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AccountCheckoutServiceExtensions.ReceiptPolicyName)]
public sealed class AccountPaymentReceiptsController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AccountPaymentReceiptDto>>> Get(Guid attemptId,
        [FromHeader(Name = AccountReceiptCredentialCrypto.HeaderName)] string? receiptCredential) =>
        Ok(await mediator.SendQuery(new GetAccountPaymentReceiptQuery(attemptId, receiptCredential),
            HttpContext.RequestAborted));
}
