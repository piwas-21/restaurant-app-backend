using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantManagementContext(IOptions<TenantBridgeSettings> bridge,
    IOptions<TenantManagementGatewaySettings> management, IOptions<UberWebhookSettings> webhook, TimeProvider clock)
{
    public TenantBridgeSettings Bridge => bridge.Value;
    public TenantManagementGatewaySettings Management => management.Value;
    public UberWebhookSettings Webhook => webhook.Value;
    public TimeProvider Clock { get; } = clock;
    public TenantStoreBinding ConfiguredStore => Bridge.Store;

    public AvailabilityBinding Binding(TenantStoreBinding? store = null)
    {
        var selected = store ?? ConfiguredStore;
        return new(Webhook.ClientId, selected.StoreId, selected.TenantId, selected.CatalogueRevision);
    }

    public void RequireEnabled()
    {
        var store = ConfiguredStore;
        if (!Management.Enabled || !Bridge.Enabled || !Bridge.UseTenantCatalogue || !Bridge.SyncAvailability
            || store.CatalogueApiToken.Length == 0 || Webhook.StoreIds.Length != 1 || Webhook.StoreIds[0] != store.StoreId)
            throw new ChannelConsoleException(404, "ModuleNotEnabled");
    }

    public TenantStoreBinding Clone(TenantStoreBinding source, string revision, IReadOnlyList<TenantItemMapping> items)
        => new()
        {
            StoreId = source.StoreId,
            TenantId = source.TenantId,
            BaseUrl = source.BaseUrl,
            ApiToken = source.ApiToken,
            CatalogueApiToken = source.CatalogueApiToken,
            Currency = source.Currency,
            CatalogueRevision = revision,
            PublishedMenuHash = source.PublishedMenuHash,
            Items = items.ToList()
        };
}
