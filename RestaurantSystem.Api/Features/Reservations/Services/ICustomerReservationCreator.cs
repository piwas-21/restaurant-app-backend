using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public interface ICustomerReservationCreator
{
    Task<ReservationCreationDraft> StageAsync(CustomerReservationCreateDto request,
        CustomerReservationOperationOwner owner, CancellationToken cancellationToken);
}
