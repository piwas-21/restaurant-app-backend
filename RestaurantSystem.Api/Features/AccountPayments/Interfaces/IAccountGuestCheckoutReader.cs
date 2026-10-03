using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Interfaces;

public interface IAccountGuestCheckoutReader
{
    Task<AccountCheckoutStartDto> ReadAsync(Guid sessionId, Guid operationId,
        string? participantCredential, CancellationToken cancellationToken);
}
