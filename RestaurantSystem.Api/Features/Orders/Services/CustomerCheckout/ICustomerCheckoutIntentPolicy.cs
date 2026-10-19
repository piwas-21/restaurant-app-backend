using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerCheckoutIntentPolicy
{
    Task EnsureAsync(CustomerCheckoutIntentDto intent, CustomerCheckoutQuoteDto quote, CancellationToken cancellationToken);
}
