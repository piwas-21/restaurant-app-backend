using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitOriginalOrderCancellationCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCancellationPreparationQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetOriginalCancellationPreparationQuery;
using RestaurantSystem.Api.Features.Orders.Queries.RecoverCustomerOrderCancellationQuery;
using RestaurantSystem.Api.Features.Orders.Queries.RecoverOriginalOrderCancellationQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/customer-mobile/orders")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileOrderCancellationsController(CustomMediator mediator) : ControllerBase
{
    [HttpGet("{orderId:guid}/cancellation")]
    [RequireRole(UserRole.Customer)]
    public async Task<ActionResult<ApiResponse<CustomerOrderCancellationPreparationDto>>> PrepareCustomer(Guid orderId,
        [FromQuery] int? schemaVersion, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetCustomerCancellationPreparationQuery(schemaVersion, orderId), cancellationToken));

    // Original capabilities stay in the body/header rather than URLs, browser history or referrers.
    [HttpPost("cancellation/original/prepare")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<CustomerOrderCancellationPreparationDto>>> PrepareOriginal(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetOriginalCancellationPreparationQuery(sessionId, proof), cancellationToken));

    [HttpPost("cancellation/submit")]
    [RequireRole(UserRole.Customer)]
    public async Task<ActionResult<ApiResponse<CustomerOrderCancellationResolutionDto>>> SubmitCustomer(
        [FromBody] CustomerOrderCancellationIntentDto intent, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new SubmitCustomerOrderCancellationCommand(intent), cancellationToken));

    [HttpPost("cancellation/original/submit")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<CustomerOrderCancellationResolutionDto>>> SubmitOriginal(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerOrderCancellationGuestDto request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new SubmitOriginalOrderCancellationCommand(sessionId, request), cancellationToken));

    [HttpPost("cancellation/recover")]
    [RequireRole(UserRole.Customer)]
    public async Task<ActionResult<ApiResponse<CustomerOrderCancellationResolutionDto>>> RecoverCustomer(
        [FromBody] CustomerOrderCancellationIntentDto intent, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new RecoverCustomerOrderCancellationQuery(intent), cancellationToken));

    [HttpPost("cancellation/original/recover")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<CustomerOrderCancellationResolutionDto>>> RecoverOriginal(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerOrderCancellationGuestDto request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new RecoverOriginalOrderCancellationQuery(sessionId, request), cancellationToken));
}
