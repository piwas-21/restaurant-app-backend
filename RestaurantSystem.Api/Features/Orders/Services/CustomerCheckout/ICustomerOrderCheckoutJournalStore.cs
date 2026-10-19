using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutJournalStore
{
    Task<CustomerOrderCheckoutJournal> FreezeAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken);
}
