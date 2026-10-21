using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public interface ICustomerReservationProofService
{
    string Create(Guid tenantId, Guid operationId, CustomerReservationOperationOwner owner);
    void Require(CustomerReservationProofDto proof, CustomerReservationOperationOwner owner);
}
