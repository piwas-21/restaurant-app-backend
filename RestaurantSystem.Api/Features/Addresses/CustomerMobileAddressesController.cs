using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.Addresses.Queries.GetCustomerAddressesQuery;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Addresses;

[ApiController]
[Route("api/customer-mobile/account/addresses")]
[RequireRole(UserRole.Customer)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerMobileAddressesController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerAddressPageDto>>> Read(
        [FromQuery] GetCustomerAddressesQuery query, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(query, cancellationToken));
}
