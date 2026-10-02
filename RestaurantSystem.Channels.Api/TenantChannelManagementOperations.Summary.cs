using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelManagementOperations
{
    public async Task<JsonElement> Summary(CancellationToken cancellationToken)
    {
        RequireEnabled();
        var store = ConfiguredStore; var binding = Binding(store);
        var disconnected = await _connectionState.Read(binding, cancellationToken);
        var latest = await _publications.Latest(binding, cancellationToken);
        JsonElement? config = null; string? degradedReason = null;
        try { config = await _connection.Configuration(cancellationToken); }
        catch (ChannelConsoleException) { degradedReason = "ProviderHealthUnavailable"; }
        var enabled = config is { } value && ProviderJson.Flag(value, "enabled");
        var manager = config is { } data && ProviderJson.Flag(data, "orderManager");
        var pending = config is { } state && ProviderJson.Flag(state, "pending");
        var manual = config is { } acceptance ? ProviderJson.OptionalFlag(acceptance, "manualAcceptance") : null;
        var returnedStore = config is { } actual ? ProviderJson.Text(actual, "storeId") : string.Empty;
        var storeConfirmed = returnedStore == store.StoreId.ToString("D") && enabled && manager && !pending;
        var connectionStatus = disconnected.IsDisconnected ? "notConnected" : config is null ? "needsAttention"
            : pending ? "authorizing" : enabled && manager ? "connected" : "notConnected";
        var availability = await _availability.Read(cancellationToken);
        var availabilityFresh = availability.TryGetProperty("items", out var rows) && rows.ValueKind == JsonValueKind.Array
            && rows.GetArrayLength() > 0 && rows.EnumerateArray().All(row => row.TryGetProperty("fresh", out var fresh) && fresh.ValueKind == JsonValueKind.True);
        var publicationHealthy = latest is null || latest.State == CataloguePublicationStates.Verified;
        var health = config is null ? "unavailable" : connectionStatus != "connected" || !publicationHealthy || !availabilityFresh
            ? "degraded" : "healthy";
        if (disconnected.IsDisconnected) degradedReason = "IntegrationDisconnected";
        else if (!publicationHealthy) degradedReason = "MenuPublicationUnconfirmed";
        else if (!availabilityFresh) degradedReason = "AvailabilityStale";
        DateTimeOffset? checkedAt = config is null ? null : _clock.GetUtcNow();
        return ProviderJson.Encode(new
        {
            provider = "uber-eats",
            enabled = true,
            sandboxOnly = true,
            connectionStatus,
            healthStatus = health,
            storeId = store.StoreId,
            currency = store.Currency,
            storeConfirmed,
            storeDisplayName = (string?)null,
            integrationEnabled = enabled,
            isOrderManager = manager,
            pendingMerchantActivation = pending,
            requireManualAcceptance = manual,
            paused = ProviderJson.Flag(availability, "paused"),
            checkedAt,
            degradedReason,
            capabilities = new
            {
                supportsSimpleItems = true,
                supportsVariations = true,
                supportsModifiers = false,
                supportsBundles = false,
                supportsItemAvailability = _bridge.Value.SyncAvailability,
                supportsStoreHoursEditing = false,
                supportsAutomaticAcceptance = false
            },
            latestPublication = PublicationSummary(latest)
        });
    }
}
