using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public sealed record CustomerReservationItemDto(Guid ReservationId, DateOnly ReservationDate,
    TimeSpan StartTime, TimeSpan EndTime, int NumberOfGuests, ReservationStatus Status, string? SpecialRequests);

public sealed record CustomerReservationCursorDto(DateTime ReservationDate, TimeSpan StartTime, Guid ReservationId);

public sealed record CustomerReservationsPageDto(int SchemaVersion, string TimeZone,
    IReadOnlyList<CustomerReservationItemDto> Items, CustomerReservationCursorDto? Next);
