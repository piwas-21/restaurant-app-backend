using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationsQuery;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
[RequireModule(ModuleIds.Reservations)]
[RequireRole(UserRole.Customer)]
[Route("api/customer-mobile/reservations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileReservationsController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CustomerReservationsPageDto>>> History(
        [FromQuery] GetCustomerReservationsQuery query, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(query, cancellationToken));

    [HttpGet("{reservationId:guid}")]
    public async Task<ActionResult<ApiResponse<CustomerReservationItemDto>>> Detail(Guid reservationId,
        [FromQuery] int? schemaVersion, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetCustomerReservationQuery(schemaVersion, reservationId), cancellationToken));
}
