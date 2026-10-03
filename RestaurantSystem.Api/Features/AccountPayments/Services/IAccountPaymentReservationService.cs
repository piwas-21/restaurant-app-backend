using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentReservationService
{
    Task<AccountPaymentOperationDto> ReserveAsync(
        Guid sessionId, Guid operationId, ReserveAccountPaymentRequest request, CancellationToken cancellationToken);

    Task<AccountPaymentOperationDto> ReleaseAsync(
        Guid sessionId, Guid operationId, ReleaseAccountPaymentRequest request, CancellationToken cancellationToken);
}
