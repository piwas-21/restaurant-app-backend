using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

public interface ICustomerOrderCancellationRules
{
    Task<CustomerOrderCancellationReason?> RefusalUnderLockAsync(Order order, CancellationToken cancellationToken);
}
