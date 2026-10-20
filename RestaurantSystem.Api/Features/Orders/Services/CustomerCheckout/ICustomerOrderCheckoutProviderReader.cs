using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutProviderReader
{
    StripeProviderContext CurrentContext();
    Task<StripeCheckoutSession> ReadAsync(CheckoutSessionRequest request, string sessionId,
        CancellationToken cancellationToken);
}
