using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutStatusReader
{
    Task<AccountCheckoutStartDto> ReadAsync(Guid attemptId, string? canonicalCheckoutUrl,
        CancellationToken cancellationToken);
}
