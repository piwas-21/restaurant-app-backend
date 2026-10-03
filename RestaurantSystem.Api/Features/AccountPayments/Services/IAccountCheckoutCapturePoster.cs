using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutCapturePoster
{
    Task PostAsync(AccountCheckoutJournal verified, CancellationToken cancellationToken);
}
