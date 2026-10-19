using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
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
}
