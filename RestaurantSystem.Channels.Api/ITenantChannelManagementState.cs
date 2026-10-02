using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public interface ITenantCatalogueManagementState
{
    Task<CatalogueMappingDraft?> ReadDraft(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<bool> SaveDraft(AvailabilityBinding binding, CatalogueMappingDraft draft, string? expectedRevision, CancellationToken cancellationToken);
    Task<TenantStoreBinding> Active(CancellationToken cancellationToken);
    Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<CataloguePublication?> Find(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken);
    Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken);
    Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken);
}

public interface ITenantChannelAvailabilityState
{
    Task<TenantStoreBinding> Active(CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelAvailabilityState>> Read(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<ChannelAvailabilityOverride?> ReadOverride(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<ChannelAvailabilityOverride> SetOverride(AvailabilityBinding binding, bool paused, DateTimeOffset? pausedUntil,
        Guid actorId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken);
}
