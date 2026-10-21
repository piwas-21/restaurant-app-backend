using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using LegacySlots = RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery.GetAvailableTimeSlotsQuery;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationEditAvailabilityQuery;

public sealed record GetCustomerReservationEditAvailabilityQuery(int? SchemaVersion, Guid ReservationId,
    DateOnly? Date, int? NumberOfGuests, string? TimeZone) : IQuery<ApiResponse<CustomerReservationEditAvailabilityDto>>;

public sealed class GetCustomerReservationEditAvailabilityQueryHandler(ICustomerReservationEditReadAccess access,
    CustomMediator mediator, ITenantClock clock)
    : IQueryHandler<GetCustomerReservationEditAvailabilityQuery, ApiResponse<CustomerReservationEditAvailabilityDto>>
{
    private const int MaximumSlots = 48;
    public async Task<ApiResponse<CustomerReservationEditAvailabilityDto>> Handle(GetCustomerReservationEditAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        var original = await access.ReadAsync(query.ReservationId, cancellationToken);
        if (query.Date is not { } day || query.NumberOfGuests is not { } guests || guests is < 1 or > 20
            || query.TimeZone != original.Context.TimeZone || day < DateOnly.FromDateTime(clock.Now.DateTime))
            throw new BadRequestException("Choose a current restaurant date, party size and timezone.");
        var available = await mediator.SendQuery(new LegacySlots(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), guests)
        { ExcludedReservationId = query.ReservationId }, cancellationToken);
        if (!available.Success || available.Data is null)
            throw new BadRequestException("Reservation availability is unavailable. Refresh before editing.");
        await access.RevalidateAsync(original, cancellationToken);
        var now = clock.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        var slots = available.Data.TimeSlots.Where(slot => slot.StartTime >= TimeSpan.Zero
                && slot.EndTime <= TimeSpan.FromDays(1) && slot.EndTime > slot.StartTime
                && (day > today || day == today && slot.StartTime > now.TimeOfDay)
                && CustomerReservationAssignedSlots.Fits(slot.AvailableTables, original.AssignedTables, guests))
            .Select(slot => new CustomerReservationSlotDto(slot.StartTime, slot.EndTime)).Distinct()
            .OrderBy(slot => slot.StartTime).ThenBy(slot => slot.EndTime).Take(MaximumSlots).ToArray();
        return ApiResponse<CustomerReservationEditAvailabilityDto>.SuccessWithData(
            new(1, query.ReservationId, day, original.Context.TimeZone, guests, slots));
    }
}
