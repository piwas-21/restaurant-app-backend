using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public interface ICustomerReservationOperationService
{
    Task<CustomerReservationResolutionDto> CreateAsync(string sessionId, CustomerReservationCreateDto request,
        CancellationToken cancellationToken);
    Task<CustomerReservationResolutionDto> ResolveAsync(string sessionId, CustomerReservationProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken);
}
