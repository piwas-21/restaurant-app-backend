namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>Fresh confirmation details; the protected proof contains no customer contact.</summary>
public sealed record CustomerReservationCancellationPreparedDto(int SchemaVersion, CustomerReservationProofDto Proof,
    CustomerReservationItemDto Reservation, string TimeZone);
