using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

internal static class ReservationDtoMapper
{
    public static ReservationDto ToDto(Reservation reservation, string tableNumber) => new()
    {
        Id = reservation.Id,
        CustomerId = reservation.CustomerId,
        CustomerName = reservation.CustomerName,
        CustomerEmail = reservation.CustomerEmail,
        CustomerPhone = reservation.CustomerPhone ?? string.Empty,
        TableId = reservation.TableId,
        TableNumber = tableNumber,
        ReservationDate = reservation.ReservationDate,
        StartTime = reservation.StartTime,
        EndTime = reservation.EndTime,
        NumberOfGuests = reservation.NumberOfGuests,
        Status = reservation.Status,
        SpecialRequests = reservation.SpecialRequests,
        Notes = reservation.Notes,
        CreatedAt = reservation.CreatedAt
    };
}
