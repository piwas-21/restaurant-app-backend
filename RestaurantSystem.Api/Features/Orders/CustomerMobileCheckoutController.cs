using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CancelCustomerOrderCommand;
using RestaurantSystem.Api.Features.Orders.Queries.PrepareCustomerCheckoutQuery;
using RestaurantSystem.Api.Features.Orders.Queries.RecoverCustomerOrderQuery;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCheckoutQuoteQuery;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/customer-mobile/checkout")]
public sealed class CustomerMobileCheckoutController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("quote")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerCheckoutQuoteDto>>> Quote(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerCheckoutReviewDto review,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetCustomerCheckoutQuoteQuery(sessionId, review), cancellationToken));

    [HttpPost("prepare")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerCheckoutPreparationDto>>> Prepare(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerCheckoutIntentDto request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new PrepareCustomerCheckoutQuery(sessionId, request), cancellationToken));

    [HttpPost("submit")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerOrderResolutionDto>>> Submit(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerCheckoutSubmissionDto request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new SubmitCustomerCheckoutCommand(sessionId, request), cancellationToken));

    [HttpPost("recover")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerOrderResolutionDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerOrderRecoveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new RecoverCustomerOrderQuery(sessionId, request), cancellationToken));

    [HttpPost("cancel")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerOrderResolutionDto>>> Cancel(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerOrderRecoveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new CancelCustomerOrderCommand(sessionId, request), cancellationToken));
}
