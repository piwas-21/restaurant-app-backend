using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed record CustomerReservationCancellationBinding(Guid ReservationId, Guid Revision, string TimeZone);

public interface ICustomerReservationCancellationProofs
{
    CustomerReservationProofDto Create(Guid tenantId, Guid operationId, CustomerReservationOperationOwner owner,
        CustomerReservationCancellationBinding binding);
    CustomerReservationCancellationBinding Require(CustomerReservationProofDto proof, CustomerReservationOperationOwner owner);
}
