using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

/// <summary>Only independently verified, immutable mapping intent may be used by operational workers.</summary>
public sealed class CatalogueMappingResolver(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    ICataloguePublications publications, ICatalogueMappingHistory history) : ICatalogueMappingResolver
{
    private TenantStoreBinding Configured => settings.Value.Store;
    private AvailabilityBinding Binding => new(webhook.Value.ClientId, Configured.StoreId, Configured.TenantId, Configured.CatalogueRevision);

    public async Task<TenantStoreBinding> Active(CancellationToken cancellationToken)
    {
        if (!settings.Value.UseTenantCatalogue) return Configured;
        var latest = await publications.Latest(Binding, cancellationToken);
        // A pending upload may already have changed Uber's menu. Do not discover new orders against an older map.
        if (latest is not { State: CataloguePublicationStates.Verified, ProviderHash: not null, VerifiedAt: not null }) throw Unconfirmed();
        return Resolve(latest);
    }

    public async Task<TenantStoreBinding> ForRevision(string catalogueRevision, CancellationToken cancellationToken)
    {
        if (!settings.Value.UseTenantCatalogue)
            return catalogueRevision == Configured.CatalogueRevision ? Configured : throw Unconfirmed();
        return Resolve(await history.FindVerified(Binding, catalogueRevision, cancellationToken) ?? throw Unconfirmed());
    }

    private TenantStoreBinding Resolve(CataloguePublication publication)
    {
        if (publication.MappingSnapshot is not { } snapshot)
        {
            // SQL007's deployment-reviewed publications predate durable mappings. Never infer their identity by name.
            if (publication.MappingHash != CatalogueMenuPlanner.MappingHash(Configured)) throw Unconfirmed();
            return Configured;
        }
        var revision = ProviderJson.Text(snapshot, "catalogueRevision");
        if (revision.Length is < 1 or > 128 || revision.Any(char.IsControl)
            || !snapshot.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() is < 1 or > 200) throw Unconfirmed();
        var items = rows.EnumerateArray().Select(Item).ToList();
        if (items.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != items.Count
            || items.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != items.Count) throw Unconfirmed();
        var result = new TenantStoreBinding
        {
            StoreId = Configured.StoreId,
            TenantId = Configured.TenantId,
            BaseUrl = Configured.BaseUrl,
            ApiToken = Configured.ApiToken,
            CatalogueApiToken = Configured.CatalogueApiToken,
            Currency = Configured.Currency,
            CatalogueRevision = revision,
            PublishedMenuHash = publication.ProviderHash ?? throw Unconfirmed(),
            Items = items
        };
        if (CatalogueMenuPlanner.MappingHash(result) != publication.MappingHash) throw Unconfirmed();
        return result;
    }

    private static TenantItemMapping Item(JsonElement row)
    {
        var itemId = ProviderJson.Text(row, "providerItemId");
        if (itemId.Length is < 1 or > 128 || itemId.Any(char.IsControl)
            || !Guid.TryParseExact(ProviderJson.Text(row, "productId"), "D", out var product) || product == Guid.Empty)
            throw Unconfirmed();
        Guid? variation = null;
        if (!row.TryGetProperty("variationId", out var variant)) throw Unconfirmed();
        if (variant.ValueKind != JsonValueKind.Null)
        {
            if (variant.ValueKind != JsonValueKind.String || !Guid.TryParseExact(variant.GetString(), "D", out var id) || id == Guid.Empty)
                throw Unconfirmed();
            variation = id;
        }
        string? name = null;
        if (!row.TryGetProperty("variationName", out var variantName)) throw Unconfirmed();
        if (variantName.ValueKind != JsonValueKind.Null)
        {
            if (variantName.ValueKind != JsonValueKind.String) throw Unconfirmed();
            name = variantName.GetString();
        }
        if (variation.HasValue != (name is not null) || name is not null
            && (string.IsNullOrWhiteSpace(name) || name.Length > TenantCatalogueLimits.VariationNameLength || name.Any(char.IsControl))) throw Unconfirmed();
        return new() { ProviderItemId = itemId, ProductId = product, VariationId = variation, VariationName = name };
    }

    private static ChannelConsoleException Unconfirmed() => new(409, "The marketplace mapping is unconfirmed. Review menu publication before processing new orders.");
}
