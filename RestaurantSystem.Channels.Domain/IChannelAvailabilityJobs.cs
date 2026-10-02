namespace RestaurantSystem.Channels.Domain;

public sealed record AvailabilityBinding(string ClientId, Guid StoreId, string TenantId, string CatalogueRevision);
public sealed record ChannelAvailabilityDesired(string ProviderItemId, bool Available, string Reason);
public sealed record ChannelAvailabilityState(string ProviderItemId, string SourceRevision, bool DesiredAvailable,
    string SourceReason, string State, bool? ObservedAvailable, string? ProviderHash, DateTimeOffset? VerifiedAt);

public interface IChannelAvailabilityJobs
{
    Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelAvailabilityState>> Read(AvailabilityBinding binding, CancellationToken cancellationToken);
}

public interface IChannelAvailabilityLease : IAsyncDisposable
{
    Task<bool> Queue(string sourceRevision, IReadOnlyList<ChannelAvailabilityDesired> items,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> Observe(string itemId, string sourceRevision, string state, bool? observedAvailable,
        string? providerHash, DateTimeOffset now, CancellationToken cancellationToken);
}
