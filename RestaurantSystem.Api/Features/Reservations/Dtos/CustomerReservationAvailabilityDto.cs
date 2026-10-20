namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>Advisory serving times only; no table identities, layout, notes or credentials.</summary>
public sealed record CustomerReservationAvailabilityDto(int SchemaVersion, DateOnly Date,
    string TimeZone, int NumberOfGuests, IReadOnlyList<CustomerReservationSlotDto> Slots);

public sealed record CustomerReservationSlotDto(TimeSpan StartTime, TimeSpan EndTime);
