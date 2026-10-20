using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

internal sealed record CustomerReservationReadRow(Guid Id, DateTime Date, TimeSpan Start,
    TimeSpan End, int Guests, ReservationStatus Status, string? SpecialRequests);

internal static class CustomerReservationProjection
{
    internal static IQueryable<CustomerReservationReadRow> Select(IQueryable<Reservation> rows) =>
        rows.Select(row => new CustomerReservationReadRow(row.Id, row.ReservationDate, row.StartTime,
            row.EndTime, row.NumberOfGuests, row.Status, row.SpecialRequests));

    // A reservation day is stored at UTC midnight as a calendar marker. Convert after reading;
    // neither the tenant zone nor PostgreSQL's session zone may move it to another calendar day.
    internal static CustomerReservationItemDto ToDto(CustomerReservationReadRow row) => new(row.Id,
        DateOnly.FromDateTime(row.Date), row.Start, row.End, row.Guests, row.Status, row.SpecialRequests);
}
