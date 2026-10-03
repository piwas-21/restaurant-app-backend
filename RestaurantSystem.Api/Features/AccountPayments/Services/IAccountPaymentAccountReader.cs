using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentAccountReader
{
    Task<AccountPaymentAccountDto> GetAsync(Guid sessionId, CancellationToken cancellationToken);
}
