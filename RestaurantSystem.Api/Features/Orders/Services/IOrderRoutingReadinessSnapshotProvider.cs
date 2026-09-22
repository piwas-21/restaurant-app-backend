namespace RestaurantSystem.Api.Features.Orders.Services;

internal interface IOrderRoutingReadinessSnapshotProvider
{
    Task<OrderRoutingReadinessSnapshot> LoadAsync(CancellationToken cancellationToken);
}
