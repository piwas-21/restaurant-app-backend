using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderRoutingReadinessService
{
    Task ReconcileDeviceRoutesAsync(string deviceId, CancellationToken cancellationToken);

    Task ReconcileReadinessAsync(Order order, CancellationToken cancellationToken);
}
