using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantCatalogueManagementState(ICatalogueMappingDrafts drafts, ICataloguePublications publications,
    ICatalogueMappingResolver mappings, IChannelAvailabilityJobs jobs) : ITenantCatalogueManagementState
{
    public Task<CatalogueMappingDraft?> ReadDraft(AvailabilityBinding binding, CancellationToken cancellationToken)
        => drafts.Read(binding, cancellationToken);
    public Task<bool> SaveDraft(AvailabilityBinding binding, CatalogueMappingDraft draft, string? expectedRevision,
        CancellationToken cancellationToken) => drafts.Save(binding, draft, expectedRevision, cancellationToken);
    public Task<TenantStoreBinding> Active(CancellationToken cancellationToken) => mappings.Active(cancellationToken);
    public Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken)
        => publications.Latest(binding, cancellationToken);
    public Task<CataloguePublication?> Find(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
        => publications.Find(binding, id, cancellationToken);
    public Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now,
        CancellationToken cancellationToken) => publications.Verify(binding, id, providerHash, now, cancellationToken);
    public Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
        => publications.Abandon(binding, id, cancellationToken);
    public Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken)
        => jobs.TryLease(binding, cancellationToken);
}

public sealed class TenantChannelAvailabilityState(ICatalogueMappingResolver mappings, IChannelAvailabilityJobs jobs,
    IChannelAvailabilityOverrides overrides) : ITenantChannelAvailabilityState
{
    public Task<TenantStoreBinding> Active(CancellationToken cancellationToken) => mappings.Active(cancellationToken);
    public Task<IReadOnlyList<ChannelAvailabilityState>> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
        => jobs.Read(binding, cancellationToken);
    public Task<ChannelAvailabilityOverride?> ReadOverride(AvailabilityBinding binding, CancellationToken cancellationToken)
        => overrides.Read(binding, cancellationToken);
    public Task<ChannelAvailabilityOverride> SetOverride(AvailabilityBinding binding, bool paused, DateTimeOffset? pausedUntil,
        Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
        => overrides.Set(binding, paused, pausedUntil, actorId, now, cancellationToken);
    public Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken)
        => jobs.TryLease(binding, cancellationToken);
}
