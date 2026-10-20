using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>Sends the booking's rejection email after cancellation commits. Provider failures
/// are best effort and must not turn the committed cancellation into a failure.</summary>
public interface IReservationRejectedMailer
{
    Task SendAsync(Reservation reservation, CancellationToken cancellationToken);
}
