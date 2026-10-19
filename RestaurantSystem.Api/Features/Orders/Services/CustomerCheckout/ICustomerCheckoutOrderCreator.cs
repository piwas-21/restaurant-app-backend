using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed record CustomerCheckoutAccepted(Order Order, bool PaysOnline);

public interface ICustomerCheckoutOrderCreator
{
    /// <summary>First acceptance only, under the durable operation's owner lock and transaction.</summary>
    Task<CustomerCheckoutAccepted> CreateUnderLockAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken);
}
