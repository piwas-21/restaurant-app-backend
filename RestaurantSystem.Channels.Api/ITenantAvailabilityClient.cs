namespace RestaurantSystem.Channels.Api;

public interface ITenantAvailabilityClient
{
    Task<TenantAvailabilitySnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken);
}

public sealed record TenantAvailabilitySnapshot(string Revision, IReadOnlyList<TenantAvailabilityItem> Items);
public sealed record TenantAvailabilityItem(string ProviderItemId, bool Available, string Reason);
