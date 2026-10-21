using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyBalanceQuery;
using RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyHistoryQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.FidelityPoints;

[ApiController]
[RequireModule(ModuleIds.Loyalty)]
[RequireRole(UserRole.Customer)]
[Route("api/customer-mobile/loyalty")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileLoyaltyController(CustomMediator mediator) : ControllerBase
{
    [HttpGet("balance")]
    public async Task<ActionResult<ApiResponse<CustomerLoyaltyBalanceDto>>> Balance(
        [FromQuery] GetCustomerLoyaltyBalanceQuery query, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(query, cancellationToken));

    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<CustomerLoyaltyHistoryDto>>> History(
        [FromQuery] GetCustomerLoyaltyHistoryQuery query, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(query, cancellationToken));
}
