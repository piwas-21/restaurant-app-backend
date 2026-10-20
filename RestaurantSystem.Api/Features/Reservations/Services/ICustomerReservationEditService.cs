using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public interface ICustomerReservationEditService
{
    Task<CustomerReservationEditPreparedDto> PrepareAsync(string sessionId, CustomerReservationEditDto request, CancellationToken cancellationToken);
    Task<CustomerReservationResolutionDto> SubmitAsync(string sessionId, CustomerReservationEditDto request, CancellationToken cancellationToken);
    Task<CustomerReservationResolutionDto> ResolveAsync(string sessionId, CustomerReservationProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken);
}
