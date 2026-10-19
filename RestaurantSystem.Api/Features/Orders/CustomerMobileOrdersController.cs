using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetCustomerOrderReceiptQuery;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/customer-mobile/orders")]
public sealed class CustomerMobileOrdersController(CustomMediator mediator) : ControllerBase
{
    // Read-only POST keeps the original operation capability out of URL/history/access logs.
    [HttpPost("receipt")]
    [AllowAnonymous]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerOrderReceiptDto>>> Receipt(
        [FromHeader(Name = "X-Session-Id")] string sessionId, [FromBody] CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetCustomerOrderReceiptQuery(sessionId, proof), cancellationToken));
}
