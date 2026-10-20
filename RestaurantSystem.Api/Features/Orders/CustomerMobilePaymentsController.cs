using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Commands.RecoverCustomerPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/customer-mobile/checkout/payment")]
public sealed class CustomerMobilePaymentsController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("recover")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerOrderPaymentDto>>> Recover(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new RecoverCustomerPaymentCommand(sessionId, proof), cancellationToken));
}
