using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed record CustomerReservationEditBinding(Guid ReservationId, Guid Revision, string TimeZone, string Currency);
public interface ICustomerReservationEditProofs
{
    CustomerReservationProofDto Create(CustomerReservationEditDto request, CustomerReservationOperationOwner owner,
        CustomerReservationEditBinding binding);
    CustomerReservationEditBinding Require(CustomerReservationProofDto proof, CustomerReservationOperationOwner owner);
    void RequireInput(CustomerReservationEditDto request, CustomerReservationOperationOwner owner, CustomerReservationEditBinding binding);
}
