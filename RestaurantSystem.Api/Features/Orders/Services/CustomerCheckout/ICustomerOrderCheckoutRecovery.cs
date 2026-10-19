using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutRecovery
{
    Task<StripeCheckoutSession> RecoverAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken);
}
