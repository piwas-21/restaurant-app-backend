using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public interface ICustomerReservationCancellationService
{
    Task<CustomerReservationCancellationPreparedDto> PrepareAsync(string sessionId, CustomerReservationCancellationDto request,
        CancellationToken cancellationToken);
    Task<CustomerReservationResolutionDto> SubmitAsync(string sessionId, CustomerReservationProofDto proof,
        CancellationToken cancellationToken);
    Task<CustomerReservationResolutionDto> ResolveAsync(string sessionId, CustomerReservationProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken);
}
