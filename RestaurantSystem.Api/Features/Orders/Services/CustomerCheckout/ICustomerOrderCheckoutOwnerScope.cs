using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutOwnerScope
{
    Task<T> RunAsync<T>(string sessionId, CustomerOrderRecoveryDto proof,
        Func<CustomerOrderCheckoutJournal, Order, DateTime, Task<T>> work, CancellationToken cancellationToken);
}
