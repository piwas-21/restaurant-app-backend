using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class ChannelAvailabilityStatus(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    IChannelAvailabilityJobs jobs, TimeProvider clock, ICatalogueMappingResolver? mappings = null, IChannelAvailabilityOverrides? overrides = null) : IChannelAvailabilityStatus
{
    public async Task<JsonElement> Read(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        var enabled = options.Enabled && !options.Paused && options.SyncAvailability;
        var store = options.Store;
        if (enabled && mappings is not null)
        {
            try { store = await mappings.Active(cancellationToken); }
            catch (ChannelConsoleException) { enabled = false; }
        }
        var binding = new AvailabilityBinding(webhook.Value.ClientId, store.StoreId, store.TenantId, store.CatalogueRevision);
        var intent = overrides is null || !options.Enabled ? null : await overrides.Read(binding, cancellationToken);
        var paused = intent is { IsPaused: true } && (intent.PausedUntil is null || intent.PausedUntil > clock.GetUtcNow());
        // Disabled deployments need no new table. Never leak either machine credential or provider payload.
        var rows = options.SyncAvailability ? await jobs.Read(new(webhook.Value.ClientId, store.StoreId,
            store.TenantId, store.CatalogueRevision), cancellationToken) : [];
        return ProviderJson.Encode(new
        {
            enabled,
            paused = options.Paused || paused,
            pausedUntil = paused ? intent!.PausedUntil : null,
            checkedAt = clock.GetUtcNow(),
            catalogueRevision = store.CatalogueRevision,
            items = rows.Select(row => new
            {
                itemId = row.ProviderItemId,
                desiredAvailable = row.DesiredAvailable,
                sourceReason = row.SourceReason,
                state = row.State,
                observedAvailable = row.ObservedAvailable,
                verifiedAt = row.VerifiedAt,
                sourceRevision = row.SourceRevision,
                fresh = enabled && row.State == "Verified" && row.ObservedAvailable == row.DesiredAvailable
                    && (intent is null || (row.SourceReason == "ManagerPaused") == paused && row.VerifiedAt >= intent.UpdatedAt)
                    && row.VerifiedAt is { } verified && verified <= clock.GetUtcNow()
                    && clock.GetUtcNow() - verified <= TimeSpan.FromSeconds(options.AvailabilityPollSeconds * 2),
            }),
        });
    }
}
