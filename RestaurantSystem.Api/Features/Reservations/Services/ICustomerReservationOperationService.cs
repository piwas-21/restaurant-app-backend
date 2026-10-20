using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public interface ICustomerReservationOperationService
{
    Task<CustomerReservationResolutionDto> RunAsync(string sessionId, CustomerReservationCreateDto request,
        bool cancelAttempt, CancellationToken cancellationToken);
}
