using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelManagementOperations : ITenantChannelManagementOperations
{
    private readonly IOptions<TenantBridgeSettings> _bridge;
    private readonly IOptions<TenantManagementGatewaySettings> _management;
    private readonly IOptions<UberWebhookSettings> _webhook;
    private readonly ISandboxConnection _connection;
    private readonly ISandboxMenu _menu;
    private readonly ITenantCatalogueClient _tenantCatalogue;
    private readonly ITenantOAuthFlows _oauthFlows;
    private readonly ITenantCataloguePublication _publication;
    private readonly ICataloguePublications _publications;
    private readonly ICatalogueMappingResolver _mappingResolver;
    private readonly ICatalogueMappingDrafts _drafts;
    private readonly IChannelAvailabilityStatus _availability;
    private readonly IChannelAvailabilityOverrides _availabilityOverrides;
    private readonly IChannelAvailabilityJobs _availabilityJobs;
    private readonly IChannelManagementAudit _audit;
    private readonly IChannelManagementConnectionState _connectionState;
    private readonly IChannelImportView _imports;
    private readonly ITenantAvailabilityClient _tenantAvailability;
    private readonly IUberAvailabilityClient _uberAvailability;
    private readonly TimeProvider _clock;

    public TenantChannelManagementOperations(IOptions<TenantBridgeSettings> bridge,
        IOptions<TenantManagementGatewaySettings> management, IOptions<UberWebhookSettings> webhook,
        ISandboxConnection connection, ISandboxMenu menu, ITenantCatalogueClient tenantCatalogue, ITenantOAuthFlows oauthFlows,
        ITenantCataloguePublication publication, ICataloguePublications publications,
        ICatalogueMappingResolver mappingResolver, ICatalogueMappingDrafts drafts,
        IChannelAvailabilityStatus availability, IChannelAvailabilityOverrides availabilityOverrides,
        IChannelAvailabilityJobs availabilityJobs, IChannelManagementAudit audit, IChannelImportView imports,
        ITenantAvailabilityClient tenantAvailability, IUberAvailabilityClient uberAvailability,
        IChannelManagementConnectionState connectionState, TimeProvider clock)
    {
        _bridge = bridge; _management = management; _webhook = webhook; _connection = connection; _menu = menu;
        _tenantCatalogue = tenantCatalogue; _oauthFlows = oauthFlows; _publication = publication; _publications = publications;
        _mappingResolver = mappingResolver; _drafts = drafts; _availability = availability;
        _availabilityOverrides = availabilityOverrides; _availabilityJobs = availabilityJobs; _audit = audit;
        _imports = imports; _tenantAvailability = tenantAvailability; _uberAvailability = uberAvailability;
        _connectionState = connectionState; _clock = clock;
    }

    private TenantStoreBinding ConfiguredStore => _bridge.Value.Store;
    private AvailabilityBinding Binding(TenantStoreBinding? store = null)
    {
        var selected = store ?? ConfiguredStore;
        return new(_webhook.Value.ClientId, selected.StoreId, selected.TenantId, selected.CatalogueRevision);
    }

    private void RequireEnabled()
    {
        if (!_management.Value.Enabled || !_bridge.Value.Enabled || !_bridge.Value.UseTenantCatalogue
            || !_bridge.Value.SyncAvailability || ConfiguredStore.CatalogueApiToken.Length == 0 || _webhook.Value.StoreIds.Length != 1
            || _webhook.Value.StoreIds[0] != ConfiguredStore.StoreId)
            throw new ChannelConsoleException(404, "ModuleNotEnabled");
    }

    private TenantStoreBinding Clone(TenantStoreBinding source, string revision, IReadOnlyList<TenantItemMapping> items)
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

    private async Task<TenantStoreBinding> DraftStore(CancellationToken cancellationToken)
    {
        RequireEnabled();
        var draft = await _drafts.Read(Binding(), cancellationToken);
        if (draft is not null) return FromSnapshot(draft.Snapshot, draft.MappingRevision);
        try { return await _mappingResolver.Active(cancellationToken); }
        catch (ChannelConsoleException) { return ConfiguredStore; }
    }

    private TenantStoreBinding FromSnapshot(JsonElement snapshot, string mappingRevision)
    {
        if (snapshot.ValueKind != JsonValueKind.Object || !snapshot.TryGetProperty("catalogueRevision", out var revision)
            || revision.GetString() != mappingRevision || !snapshot.TryGetProperty("items", out var rows)
            || rows.ValueKind != JsonValueKind.Array) throw new ChannelConsoleException(409, "Saved catalogue mapping needs operator review.");
        var items = JsonSerializer.Deserialize<List<TenantItemMapping>>(rows.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (items is null || items.Count != ConfiguredStore.Items.Count
            || !items.Select(row => row.ProviderItemId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(ConfiguredStore.Items.Select(row => row.ProviderItemId))
            || items.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != items.Count
            || items.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != items.Count
            || items.Any(row => row.ProductId == Guid.Empty || row.VariationId == Guid.Empty)
            || ManagementMappingRevision(items) != mappingRevision)
            throw new ChannelConsoleException(409, "Saved catalogue mapping needs operator review.");
        return Clone(ConfiguredStore, mappingRevision, items);
    }

    private string ManagementMappingRevision(IEnumerable<TenantItemMapping> items)
        => ProviderJson.Hash(ProviderJson.Encode(new
        {
            tenantId = ConfiguredStore.TenantId,
            storeId = ConfiguredStore.StoreId,
            currency = ConfiguredStore.Currency,
            items = items.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal).Select(row => new
            { row.ProviderItemId, row.ProductId, row.VariationId })
        }));

    private static JsonElement MappingSnapshot(string revision, IEnumerable<TenantItemMapping> items)
        => ProviderJson.Encode(new
        {
            catalogueRevision = revision,
            items = items.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal)
            .Select(row => new { providerItemId = row.ProviderItemId, productId = row.ProductId, variationId = row.VariationId, variationName = row.VariationName })
        });
}
