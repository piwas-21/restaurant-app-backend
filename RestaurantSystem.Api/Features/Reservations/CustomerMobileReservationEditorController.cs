using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationEditContextQuery;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationEditAvailabilityQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations;

[ApiController]
[RequireModule(ModuleIds.Reservations)]
[RequireRole(UserRole.Customer)]
[Route("api/customer-mobile/reservations/{reservationId:guid}/edit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
public sealed class CustomerMobileReservationEditorController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CustomerReservationEditContextDto>>> Context(Guid reservationId,
        [FromQuery] int? schemaVersion, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetCustomerReservationEditContextQuery(schemaVersion, reservationId), cancellationToken));

    [HttpGet("availability")]
    public async Task<ActionResult<ApiResponse<CustomerReservationEditAvailabilityDto>>> Availability(Guid reservationId,
        [FromQuery] int? schemaVersion, [FromQuery] DateOnly? date, [FromQuery] int? numberOfGuests,
        [FromQuery] string? timeZone, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetCustomerReservationEditAvailabilityQuery(schemaVersion, reservationId,
            date, numberOfGuests, timeZone), cancellationToken));
}
