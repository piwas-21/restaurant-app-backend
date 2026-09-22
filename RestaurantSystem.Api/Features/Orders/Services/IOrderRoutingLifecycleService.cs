using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderRoutingLifecycleService
{
    Task EnsureRoutesAsync(Order order, CancellationToken cancellationToken);

    Task<bool> IsRoutingActivatedAsync(CancellationToken cancellationToken);

    Task BackfillActiveReleasedRoutesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderRoutingStateDto>> ProjectAsync(
        Guid orderId, CancellationToken cancellationToken);
}
