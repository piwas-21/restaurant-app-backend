using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Interfaces;

public interface IAccountCheckoutStartService
{
    Task<AccountCheckoutStartDto> StartGuestAsync(Guid sessionId, Guid operationId,
        int expectedVersion, string? participantCredential, string? receiptCredential,
        CancellationToken cancellationToken);
}
