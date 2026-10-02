using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class ChannelAvailabilityProcessor(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    ITenantAvailabilityClient tenant, IUberAvailabilityClient provider, IChannelAvailabilityJobs jobs, TimeProvider clock)
    : IChannelAvailabilityProcessor
{
    public async Task Process(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled || options.Paused || !options.SyncAvailability) return;
        var store = options.Store;
        if (webhook.Value.StoreIds.Length != 1 || webhook.Value.StoreIds[0] != store.StoreId || store.CatalogueApiToken.Length == 0)
            throw new ChannelConsoleException(409, "Availability requires the approved sandbox store and dedicated tenant read token.");
        var binding = new AvailabilityBinding(webhook.Value.ClientId, store.StoreId, store.TenantId, store.CatalogueRevision);
        await using var lease = await jobs.TryLease(binding, cancellationToken);
        if (lease is null) return;
        var desired = await tenant.Read(store, cancellationToken);
        if (!await lease.Queue(desired.Revision, desired.Items.Select(item => new ChannelAvailabilityDesired(
            item.ProviderItemId, item.Available, item.Reason)).ToArray(), clock.GetUtcNow(), cancellationToken))
            throw new ChannelConsoleException(409, "Availability metadata belongs to a different tenant binding.");
        UberAvailabilitySnapshot actual;
        try { actual = await provider.Read(store, webhook.Value.ClientId, cancellationToken); }
        catch
        {
            foreach (var item in desired.Items)
                await lease.Observe(item.ProviderItemId, desired.Revision, "Uncertain", null, null, clock.GetUtcNow(), cancellationToken);
            throw;
        }
        await Reconcile(lease, store, desired, actual, options.AvailabilityMaxWrites, cancellationToken);
    }

    private async Task Reconcile(IChannelAvailabilityLease lease, TenantStoreBinding store, TenantAvailabilitySnapshot desired,
        UberAvailabilitySnapshot actual, int maximumWrites, CancellationToken cancellationToken)
    {
        var writes = 0;
        foreach (var item in desired.Items)
        {
            if (actual.Items[item.ProviderItemId] == item.Available)
            {
                await Observe(lease, desired.Revision, item, actual, cancellationToken);
                continue;
            }
            if (writes >= maximumWrites)
            {
                await lease.Observe(item.ProviderItemId, desired.Revision, "Pending", actual.Items[item.ProviderItemId],
                    actual.Hash, clock.GetUtcNow(), cancellationToken);
                continue;
            }
            // Source changes during a bounded cycle must be reconciled before sending an obsolete intent.
            if ((await tenant.Read(store, cancellationToken)).Revision != desired.Revision) return;
            // Persist uncertainty before the outbound write; a timeout/restart must never look verified.
            if (!await lease.Observe(item.ProviderItemId, desired.Revision, "Uncertain", actual.Items[item.ProviderItemId],
                actual.Hash, clock.GetUtcNow(), cancellationToken))
                throw new ChannelConsoleException(409, "Availability revision changed before dispatch.");
            writes++;
            await provider.Update(store, item, cancellationToken);
            actual = await provider.Read(store, webhook.Value.ClientId, cancellationToken);
            await Observe(lease, desired.Revision, item, actual, cancellationToken);
        }
    }

    private Task<bool> Observe(IChannelAvailabilityLease lease, string revision, TenantAvailabilityItem item,
        UberAvailabilitySnapshot actual, CancellationToken cancellationToken)
        => lease.Observe(item.ProviderItemId, revision, actual.Items[item.ProviderItemId] == item.Available ? "Verified" : "Mismatch",
            actual.Items[item.ProviderItemId], actual.Hash, clock.GetUtcNow(), cancellationToken);
}
