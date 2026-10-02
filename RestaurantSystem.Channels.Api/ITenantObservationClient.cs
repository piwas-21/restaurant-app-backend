namespace RestaurantSystem.Channels.Api;

public interface ITenantObservationClient
{
    Task<bool> Observe(TenantStoreBinding store, Guid tenantOrderId, TenantOrderObservation observation, CancellationToken cancellationToken);
}
