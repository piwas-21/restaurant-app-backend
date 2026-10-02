using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelAvailabilityPolicy(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    ITenantAvailabilityClient tenant, ICatalogueMappingResolver mappings, IChannelAvailabilityOverrides overrides,
    TimeProvider clock) : IChannelAvailabilityPolicy
{
    public async Task<ChannelAvailabilityPlan?> Resolve(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled || options.Paused || !options.SyncAvailability) return null;
        var store = await mappings.Active(cancellationToken);
        if (webhook.Value.StoreIds.Length != 1 || webhook.Value.StoreIds[0] != store.StoreId || store.CatalogueApiToken.Length == 0)
            throw new ChannelConsoleException(409, "Availability requires the approved sandbox store and dedicated tenant read token.");
        var binding = new AvailabilityBinding(webhook.Value.ClientId, store.StoreId, store.TenantId, store.CatalogueRevision);
        return new(store, binding, webhook.Value.ClientId, options.AvailabilityMaxWrites);
    }

    public async Task<TenantAvailabilitySnapshot> Desired(ChannelAvailabilityPlan plan, CancellationToken cancellationToken)
    {
        var source = await tenant.Read(plan.Store, cancellationToken);
        var intent = await overrides.Read(plan.Binding, cancellationToken);
        if (intent is null) return source;
        var paused = intent.IsPaused && (intent.PausedUntil is null || intent.PausedUntil > clock.GetUtcNow());
        // Expiry and explicit resume invalidate prior paused observations even when source stock is unchanged.
        var revision = ProviderJson.Hash(ProviderJson.Encode(new
        { sourceRevision = source.Revision, paused, updatedAt = intent.UpdatedAt, pausedUntil = intent.PausedUntil }));
        return new(revision, paused ? source.Items.Select(item => item with { Available = false, Reason = "ManagerPaused" }).ToArray() : source.Items);
    }
}
