using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentCaptureService
{
    Task<AccountPaymentOperationDto> CaptureManualAsync(Guid sessionId, Guid operationId,
        CaptureAccountPaymentRequest request, CancellationToken cancellationToken);
}
