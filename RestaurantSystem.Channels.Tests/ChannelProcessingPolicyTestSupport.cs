using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Tests;

internal static class ChannelProcessingPolicyTestSupport
{
    internal static IChannelAvailabilityPolicy Availability(TenantBridgeSettings settings, UberWebhookSettings webhook,
        ITenantAvailabilityClient tenant, ICatalogueMappingResolver mappings,
        IChannelAvailabilityOverrides overrides, TimeProvider clock)
        => new TenantChannelAvailabilityPolicy(Options.Create(settings), Options.Create(webhook), tenant, mappings, overrides, clock);

    internal static ITenantChannelOrderProcessingPolicy OrderProcessing(TenantBridgeSettings settings,
        UberWebhookSettings webhook, ICatalogueMappingResolver mappings, IChannelManagementConnectionState connection,
        ITenantCataloguePublication catalogue)
        => new TenantChannelOrderProcessingPolicy(Options.Create(settings), Options.Create(webhook), mappings, connection, catalogue);

    internal static ICatalogueMappingResolver Mapping(TenantStoreBinding store) => new FixedMapping(store);
    internal static IChannelAvailabilityOverrides NoOverrides { get; } = new EmptyOverrides();
    internal static IChannelManagementConnectionState Connected { get; } = new ConnectedState();
    internal static ITenantCataloguePublication Catalogue { get; } = new UnavailableCatalogue();

    private sealed class FixedMapping(TenantStoreBinding store) : ICatalogueMappingResolver
    {
        public Task<TenantStoreBinding> Active(CancellationToken cancellationToken) => Task.FromResult(store);
        public Task<TenantStoreBinding> ForRevision(string catalogueRevision, CancellationToken cancellationToken)
            => catalogueRevision == store.CatalogueRevision
                ? Task.FromResult(store)
                : Task.FromException<TenantStoreBinding>(new ChannelConsoleException(409, "Unknown test catalogue revision."));
    }

    private sealed class EmptyOverrides : IChannelAvailabilityOverrides
    {
        public Task<ChannelAvailabilityOverride?> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult<ChannelAvailabilityOverride?>(null);

        public Task<ChannelAvailabilityOverride> Set(AvailabilityBinding binding, bool isPaused, DateTimeOffset? pausedUntil,
            Guid actorId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelAvailabilityOverride(isPaused, pausedUntil, actorId, updatedAt));
    }

    private sealed class ConnectedState : IChannelManagementConnectionState
    {
        public Task<ChannelManagementConnectionState> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelManagementConnectionState(false, null, null));

        public Task<ChannelManagementConnectionState> Set(AvailabilityBinding binding, bool isDisconnected, Guid actorId,
            DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelManagementConnectionState(isDisconnected, actorId, now));
    }

    private sealed class UnavailableCatalogue : ITenantCataloguePublication
    {
        public Task<JsonElement> Preview(JsonElement template, CancellationToken cancellationToken) => NotSupported();
        public Task<JsonElement> Preview(JsonElement template, TenantStoreBinding store, CancellationToken cancellationToken) => NotSupported();
        public Task<JsonElement> Publish(JsonElement template, string revision, CancellationToken cancellationToken) => NotSupported();
        public Task<JsonElement> Publish(JsonElement template, string revision, TenantStoreBinding store,
            CancellationToken cancellationToken, Func<CancellationToken, Task<bool>>? intentStillCurrent = null) => NotSupported();
        public Task<JsonElement> Expected(CancellationToken cancellationToken) => NotSupported();
        public Task RequireActive(CancellationToken cancellationToken)
            => Task.FromException(new ChannelConsoleException(409, "No reviewed test publication exists."));

        private static Task<JsonElement> NotSupported() => Task.FromException<JsonElement>(new NotSupportedException());
    }
}
