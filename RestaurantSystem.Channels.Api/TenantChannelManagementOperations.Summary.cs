using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelSummaryService(TenantManagementContext context,
    IChannelManagementConnectionState connectionState, ICataloguePublications publications,
    ISandboxConnection connection, IChannelAvailabilityStatus availability) : ITenantChannelSummaryService
{
    private const string Connected = "connected";

    public async Task<JsonElement> Summary(CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        var store = context.ConfiguredStore;
        var binding = context.Binding(store);
        var disconnected = await connectionState.Read(binding, cancellationToken);
        var latest = await publications.Latest(binding, cancellationToken);
        var provider = await ReadProviderHealth(cancellationToken);
        var connectionStatus = ConnectionStatus(provider, disconnected.IsDisconnected);
        var availabilityStatus = await availability.Read(cancellationToken);
        var fresh = AvailabilityIsFresh(availabilityStatus);
        var publicationHealthy = latest is null || latest.State == CataloguePublicationStates.Verified;
        var health = HealthStatus(provider.Configuration is not null, connectionStatus, publicationHealthy, fresh);
        var reason = DegradedReason(disconnected.IsDisconnected, publicationHealthy, fresh, provider.ErrorCode);
        var checkedAt = provider.Configuration is null ? (DateTimeOffset?)null : context.Clock.GetUtcNow();
        return ProviderJson.Encode(new
        {
            provider = "uber-eats",
            enabled = true,
            sandboxOnly = true,
            connectionStatus,
            healthStatus = health,
            storeId = store.StoreId,
            currency = store.Currency,
            storeConfirmed = ProviderStoreConfirmed(provider, store.StoreId),
            storeDisplayName = (string?)null,
            integrationEnabled = provider.Enabled,
            isOrderManager = provider.OrderManager,
            pendingMerchantActivation = provider.Pending,
            requireManualAcceptance = provider.ManualAcceptance,
            paused = ProviderJson.Flag(availabilityStatus, "paused"),
            checkedAt,
            degradedReason = reason,
            capabilities = new
            {
                supportsSimpleItems = true,
                supportsVariations = true,
                supportsModifiers = false,
                supportsBundles = false,
                supportsItemAvailability = context.Bridge.SyncAvailability,
                supportsStoreHoursEditing = false,
                supportsAutomaticAcceptance = false
            },
            latestPublication = TenantChannelCatalogueService.PublicationSummary(latest)
        });
    }

    private async Task<ProviderHealth> ReadProviderHealth(CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await connection.Configuration(cancellationToken);
            return ParseProviderHealth(configuration);
        }
        catch (ChannelConsoleException)
        {
            return ProviderHealth.Unavailable;
        }
    }

    private static ProviderHealth ParseProviderHealth(JsonElement configuration)
        => new(configuration, ProviderJson.Flag(configuration, "enabled"), ProviderJson.Flag(configuration, "orderManager"),
            ProviderJson.Flag(configuration, "pending"), ProviderJson.OptionalFlag(configuration, "manualAcceptance"),
            ProviderJson.Text(configuration, "storeId"), null);

    private static bool ProviderStoreConfirmed(ProviderHealth provider, Guid storeId)
        => provider.ReturnedStore == storeId.ToString("D") && provider.Enabled && provider.OrderManager && !provider.Pending;

    private static string ConnectionStatus(ProviderHealth provider, bool disconnected)
    {
        if (disconnected) return "notConnected";
        if (provider.Configuration is null) return "needsAttention";
        if (provider.Pending) return "authorizing";
        return provider.Enabled && provider.OrderManager ? Connected : "notConnected";
    }

    private static bool AvailabilityIsFresh(JsonElement availability)
    {
        if (!availability.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array) return false;
        return rows.GetArrayLength() > 0 && rows.EnumerateArray()
            .All(row => row.TryGetProperty("fresh", out var fresh) && fresh.ValueKind == JsonValueKind.True);
    }

    private static string HealthStatus(bool configurationAvailable, string connectionStatus, bool publicationHealthy, bool availabilityFresh)
    {
        if (!configurationAvailable) return "unavailable";
        return connectionStatus == Connected && publicationHealthy && availabilityFresh ? "healthy" : "degraded";
    }

    private static string? DegradedReason(bool disconnected, bool publicationHealthy, bool availabilityFresh, string? providerError)
    {
        if (disconnected) return "IntegrationDisconnected";
        if (!publicationHealthy) return "MenuPublicationUnconfirmed";
        if (!availabilityFresh) return "AvailabilityStale";
        return providerError;
    }

    private sealed record ProviderHealth(JsonElement? Configuration, bool Enabled, bool OrderManager,
        bool Pending, bool? ManualAcceptance, string ReturnedStore, string? ErrorCode)
    {
        internal static ProviderHealth Unavailable { get; } = new(null, false, false, false, null, string.Empty,
            "ProviderHealthUnavailable");
    }
}
