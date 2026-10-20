using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutPaymentService
{
    Task<CustomerOrderPaymentDto> RecoverAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken);
}
