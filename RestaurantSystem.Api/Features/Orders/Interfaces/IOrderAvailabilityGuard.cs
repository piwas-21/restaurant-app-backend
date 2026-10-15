using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Interfaces;

public interface IOrderAvailabilityGuard
{
    Task EnsureAvailableAsync(OrderType orderType, CancellationToken cancellationToken = default);
}
