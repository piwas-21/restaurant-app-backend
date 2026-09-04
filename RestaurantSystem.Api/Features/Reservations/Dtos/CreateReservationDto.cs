namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>
/// A guest booking. Exactly <see cref="ReservationWriteDto"/> — it adds nothing, and saying so
/// by inheriting is the point: the two write DTOs used to be one declaration written twice.
/// </summary>
public record CreateReservationDto : ReservationWriteDto;
