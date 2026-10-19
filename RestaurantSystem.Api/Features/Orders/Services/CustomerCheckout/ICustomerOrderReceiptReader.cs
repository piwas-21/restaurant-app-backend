using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderReceiptReader
{
    Task<CustomerOrderReceiptDto> ReadAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken);
    Task<CustomerOrderReceiptDto> ReadCustomerAsync(Guid orderId, CancellationToken cancellationToken);
}
