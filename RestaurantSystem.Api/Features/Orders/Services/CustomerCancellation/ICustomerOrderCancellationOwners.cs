using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

public sealed record CustomerOrderCancellationOwner(Guid OrderId, Guid? UserId, string OwnerHash);

public interface ICustomerOrderCancellationOwners
{
    CustomerOrderCancellationOwner Customer(Guid orderId);
    Task<CustomerOrderCancellationOwner> OriginalAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken);
}
