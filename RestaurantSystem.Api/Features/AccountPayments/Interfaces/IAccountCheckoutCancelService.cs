using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Interfaces;

public interface IAccountCheckoutCancelService
{
    Task<AccountCheckoutStartDto> RequestGuestAsync(Guid sessionId, Guid operationId, int expectedVersion,
        string? participantCredential, CancellationToken cancellationToken);
}
