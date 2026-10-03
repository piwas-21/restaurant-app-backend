using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentReservationService
{
    Task<AccountPaymentOperationDto> ReserveAsync(
        Guid sessionId, Guid operationId, ReserveAccountPaymentRequest request, CancellationToken cancellationToken);

    Task<AccountPaymentOperationDto> ReserveGuestAsync(
        Guid sessionId, Guid operationId, string? participantCredential,
        ReserveAccountPaymentRequest request, CancellationToken cancellationToken);

    Task<AccountPaymentOperationDto> ReleaseAsync(
        Guid sessionId, Guid operationId, ReleaseAccountPaymentRequest request, CancellationToken cancellationToken);

    Task<AccountPaymentOperationDto> ReleaseGuestAsync(
        Guid sessionId, Guid operationId, string? participantCredential,
        ReleaseAccountPaymentRequest request, CancellationToken cancellationToken);
}
