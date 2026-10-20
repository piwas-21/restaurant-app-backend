using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutProviderReader
{
    Task<StripeCheckoutSession> ReadAsync(CheckoutSessionRequest request, string sessionId,
        CancellationToken cancellationToken);
}
