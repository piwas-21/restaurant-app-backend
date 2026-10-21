using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public interface ICustomerReservationCancellationAccess
{
    CustomerReservationOperationOwner Capture(string sessionId, int? version, Guid operationId, Guid tenantId);
    Task LockCustomerAsync(CustomerReservationOperationOwner owner, CancellationToken cancellationToken);
    Task<Reservation> LockBookingAsync(Guid reservationId, CustomerReservationOperationOwner owner, CancellationToken cancellationToken);
    Task RequireAcceptingAsync(Guid tenantId, string timeZone, CancellationToken cancellationToken);
    void EnsureEditable(Reservation reservation);
    string AuditIdentifier { get; }
}
