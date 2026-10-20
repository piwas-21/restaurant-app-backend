using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationAvailabilityQuery;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
[AllowAnonymous]
[RequireModule(ModuleIds.Reservations)]
[Route("api/customer-mobile/reservations/availability")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileReservationAvailabilityController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CustomerReservationAvailabilityDto>>> Read(
        [FromQuery] GetCustomerReservationAvailabilityQuery query, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(query, cancellationToken));
}
