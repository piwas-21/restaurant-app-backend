using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using LegacySlotsQuery = RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery.GetAvailableTimeSlotsQuery;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationAvailabilityQuery;

public sealed record GetCustomerReservationAvailabilityQuery(int? SchemaVersion, DateOnly? Date,
    int NumberOfGuests) : IQuery<ApiResponse<CustomerReservationAvailabilityDto>>;

public sealed class GetCustomerReservationAvailabilityQueryHandler(CustomMediator mediator, ITenantClock clock)
    : IQueryHandler<GetCustomerReservationAvailabilityQuery, ApiResponse<CustomerReservationAvailabilityDto>>
{
    // A calendar day has at most 48 half-hour starts. The existing shared engine uses two-hour stays.
    private const int MaximumSlots = 48;
    public async Task<ApiResponse<CustomerReservationAvailabilityDto>> Handle(GetCustomerReservationAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        var original = await CustomerReservationReadAccess.RequireAvailableAsync(mediator, cancellationToken);
        if (query.Date is not { } date || date < DateOnly.FromDateTime(clock.Now.DateTime))
            throw new BadRequestException("Choose a current or future restaurant date.");
        // Calendar marker only: never convert through the device or restaurant timezone.
        var marker = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var result = await mediator.SendQuery(new LegacySlotsQuery(marker, query.NumberOfGuests), cancellationToken);
        if (!result.Success || result.Data is null)
            throw new BadRequestException("Reservation availability is unavailable. Choose another date or try again.");
        var current = await CustomerReservationReadAccess.RequireAvailableAsync(mediator, cancellationToken);
        if (current.TenantId != original.TenantId || current.Timezone != original.Timezone)
            throw new NotFoundException("Customer reservations are unavailable.");
        var slots = result.Data.TimeSlots
            .Where(slot => slot.StartTime >= TimeSpan.Zero && slot.EndTime <= TimeSpan.FromDays(1)
                && slot.EndTime > slot.StartTime
                && slot.AvailableTables.Where(table => table.MaxGuests > 0).Sum(table => (long)table.MaxGuests) >= query.NumberOfGuests)
            .Select(slot => new CustomerReservationSlotDto(slot.StartTime, slot.EndTime))
            .Distinct().OrderBy(slot => slot.StartTime).ThenBy(slot => slot.EndTime).Take(MaximumSlots).ToArray();
        return ApiResponse<CustomerReservationAvailabilityDto>.SuccessWithData(new(1, date, current.Timezone, query.NumberOfGuests, slots));
    }
}
