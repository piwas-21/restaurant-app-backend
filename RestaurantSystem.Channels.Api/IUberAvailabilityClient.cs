namespace RestaurantSystem.Channels.Api;

public interface IUberAvailabilityClient
{
    Task<UberAvailabilitySnapshot> Read(TenantStoreBinding store, string clientId, CancellationToken cancellationToken);
    Task Update(TenantStoreBinding store, TenantAvailabilityItem item, CancellationToken cancellationToken);
}

public sealed record UberAvailabilitySnapshot(string Hash, IReadOnlyDictionary<string, bool> Items);
