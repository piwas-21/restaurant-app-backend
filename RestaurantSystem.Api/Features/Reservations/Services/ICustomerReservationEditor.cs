using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed record CustomerReservationEditDraft(CustomerReservationEditReadSnapshot Original,
    Reservation Reservation, ReservationEdit Edit, string TableNumber);
public interface ICustomerReservationEditor
{
    Task<CustomerReservationEditDraft> ReviewAsync(CustomerReservationEditDto request, Reservation row,
        CustomerReservationOperationOwner owner, CancellationToken cancellationToken);
    Task RevalidateAsync(CustomerReservationEditDto request, CustomerReservationEditReadSnapshot applied,
        ReservationEdit originalEdit, CancellationToken cancellationToken);
}
