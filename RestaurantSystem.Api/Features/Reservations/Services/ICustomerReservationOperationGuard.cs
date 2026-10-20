using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed record CustomerReservationOperationOwner(Guid? CustomerId, string OwnerHash, string PayloadHash);

public interface ICustomerReservationOperationGuard
{
    CustomerReservationOperationOwner Capture(string sessionId, CustomerReservationCreateDto request);
    CustomerReservationOperationOwner CaptureProof(string sessionId, CustomerReservationProofDto proof);
    Task<CustomerReservationProofDto> PrepareAsync(string sessionId, CustomerReservationCreateDto request,
        CancellationToken cancellationToken);
    Task LockOwnerAsync(CustomerReservationOperationOwner owner, CancellationToken cancellationToken);
    Task RequireAcceptingAsync(CustomerReservationCreateDto request, CustomerReservationOperationOwner owner,
        CancellationToken cancellationToken);
    void EnsureFutureSlot(CustomerReservationCreateDto request);
}
