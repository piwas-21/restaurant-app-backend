using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class ChannelAvailabilityStatus(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    IChannelAvailabilityJobs jobs, TimeProvider clock) : IChannelAvailabilityStatus
{
    public async Task<JsonElement> Read(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        var enabled = options.Enabled && !options.Paused && options.SyncAvailability;
        // Disabled deployments need no new table. Never leak either machine credential or provider payload.
        var rows = options.SyncAvailability ? await jobs.Read(new(webhook.Value.ClientId, options.Store.StoreId,
            options.Store.TenantId, options.Store.CatalogueRevision), cancellationToken) : [];
        return ProviderJson.Encode(new
        {
            enabled,
            paused = options.Paused,
            checkedAt = clock.GetUtcNow(),
            catalogueRevision = options.Store.CatalogueRevision,
            items = rows.Select(row => new
            {
                itemId = row.ProviderItemId,
                desiredAvailable = row.DesiredAvailable,
                sourceReason = row.SourceReason,
                state = row.State,
                observedAvailable = row.ObservedAvailable,
                verifiedAt = row.VerifiedAt,
                sourceRevision = row.SourceRevision,
                fresh = enabled && row.VerifiedAt is { } verified && verified <= clock.GetUtcNow()
                    && clock.GetUtcNow() - verified <= TimeSpan.FromSeconds(options.AvailabilityPollSeconds * 2),
            }),
        });
    }
}
