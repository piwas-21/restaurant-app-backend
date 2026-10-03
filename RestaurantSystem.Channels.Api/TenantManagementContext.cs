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

    public void RequireCategorySelection() => RequireEnabled();

    public TenantStoreBinding Clone(TenantStoreBinding source, string revision, IReadOnlyList<TenantItemMapping> items,
        TenantCatalogueBindingSelection? selection = null)
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
            SourceRevision = selection?.SourceRevision ?? source.SourceRevision,
            Language = selection?.Language ?? source.Language,
            SelectedCategoryIds = selection?.SelectedCategoryIds.ToList() ?? source.SelectedCategoryIds.ToList(),
            ItemOverrides = selection?.ItemOverrides.Select(CloneOverride).ToList() ?? source.ItemOverrides.Select(CloneOverride).ToList(),
            Items = items.ToList(),
            Categories = (selection?.Categories ?? source.Categories).Select(row => new TenantCategoryMapping
            {
                CategoryId = row.CategoryId,
                ProviderCategoryId = row.ProviderCategoryId,
                Name = row.Name,
                DisplayOrder = row.DisplayOrder,
                Active = row.Active,
                TotalItemCount = row.TotalItemCount,
                SupportedItemCount = row.SupportedItemCount,
                UnsupportedItemCount = row.UnsupportedItemCount,
                SelectedItemCount = row.SelectedItemCount,
                SelectedUnsupportedItemCount = row.SelectedUnsupportedItemCount
            }).ToList()
        };

    private static TenantItemMappingOverride CloneOverride(TenantItemMappingOverride row)
        => new()
        {
            ProductId = row.ProductId,
            VariationId = row.VariationId,
            CategoryId = row.CategoryId,
            Selected = row.Selected,
            SelectionKey = row.SelectionKey,
            SourceFingerprint = row.SourceFingerprint,
            Supported = row.Supported
        };
}
