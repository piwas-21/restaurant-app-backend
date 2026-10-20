namespace RestaurantSystem.Api.Features.Reservations.Dtos;

// Private editor input only; never use this contact-bearing shape for history or general details.
public sealed record CustomerReservationContactDto(string CustomerName, string CustomerEmail, string? CustomerPhone);
public sealed record CustomerReservationEditContextDto(int SchemaVersion, Guid TenantId, string TimeZone,
    CustomerReservationItemDto Reservation, CustomerReservationContactDto Contact);
public sealed record CustomerReservationEditAvailabilityDto(int SchemaVersion, Guid ReservationId, DateOnly Date,
    string TimeZone, int NumberOfGuests, IReadOnlyList<CustomerReservationSlotDto> Slots);
