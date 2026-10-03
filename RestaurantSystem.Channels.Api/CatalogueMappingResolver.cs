using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

/// <summary>Only independently verified, immutable mapping intent may be used by operational workers.</summary>
public sealed class CatalogueMappingResolver(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    ICataloguePublications publications, ICatalogueMappingHistory history) : ICatalogueMappingResolver
{
    private const int MaximumMappingItems = 200;
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
            return Legacy(publication);
        return ResolveSnapshot(publication, snapshot);
    }

    private TenantStoreBinding Legacy(CataloguePublication publication)
    {
        // SQL007's deployment-reviewed publications predate durable mappings. Never infer their identity by name.
        if (publication.MappingHash != CatalogueMenuPlanner.MappingHash(Configured)) throw Unconfirmed();
        return Configured;
    }

    private TenantStoreBinding ResolveSnapshot(CataloguePublication publication, JsonElement snapshot)
    {
        var revision = SnapshotRevision(snapshot);
        if (TenantCatalogueCategorySnapshot.IsCategorySnapshot(snapshot))
        {
            var category = TenantCatalogueCategorySnapshot.Read(snapshot, revision);
            var categoryItems = category.Items.ToList();
            var selection = new TenantCatalogueBindingSelection(category.SourceRevision, category.Language,
                category.SelectedCategoryIds, category.ItemOverrides, category.Categories);
            return Verify(publication, CopyConfigured(revision, publication.ProviderHash, categoryItems, selection));
        }
        var items = SnapshotItems(snapshot);
        return Verify(publication, CopyConfigured(revision, publication.ProviderHash, items));
    }

    private static TenantStoreBinding Verify(CataloguePublication publication, TenantStoreBinding result)
    {
        if (CatalogueMenuPlanner.MappingHash(result) != publication.MappingHash) throw Unconfirmed();
        return result;
    }

    private static string SnapshotRevision(JsonElement snapshot)
    {
        var revision = ProviderJson.Text(snapshot, "catalogueRevision");
        if (revision.Length is < 1 or > 128 || revision.Any(char.IsControl)) throw Unconfirmed();
        return revision;
    }

    private static List<TenantItemMapping> SnapshotItems(JsonElement snapshot)
    {
        if (!snapshot.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() is < 1 or > MaximumMappingItems) throw Unconfirmed();
        var items = rows.EnumerateArray().Select(Item).ToList();
        if (items.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != items.Count
            || items.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != items.Count) throw Unconfirmed();
        return items;
    }

    private TenantStoreBinding CopyConfigured(string revision, string? providerHash, List<TenantItemMapping> items,
        TenantCatalogueBindingSelection? selection = null)
    {
        var categories = selection?.Categories ?? [];
        return new TenantStoreBinding
        {
            StoreId = Configured.StoreId,
            TenantId = Configured.TenantId,
            BaseUrl = Configured.BaseUrl,
            ApiToken = Configured.ApiToken,
            CatalogueApiToken = Configured.CatalogueApiToken,
            Currency = Configured.Currency,
            CatalogueRevision = revision,
            PublishedMenuHash = providerHash ?? throw Unconfirmed(),
            SourceRevision = selection?.SourceRevision ?? string.Empty,
            Language = selection?.Language ?? string.Empty,
            SelectedCategoryIds = selection?.SelectedCategoryIds.ToList() ?? [],
            ItemOverrides = selection?.ItemOverrides.Select(CloneOverride).ToList() ?? [],
            Items = items,
            Categories = categories.Select(CloneCategory).ToList()
        };
    }

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

    private static TenantCategoryMapping CloneCategory(TenantCategoryMapping row)
        => new()
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
        };

    private static TenantItemMapping Item(JsonElement row)
    {
        var itemId = ProviderJson.Text(row, "providerItemId");
        if (itemId.Length is < 1 or > 128 || itemId.Any(char.IsControl)
            || !Guid.TryParseExact(ProviderJson.Text(row, "productId"), "D", out var product) || product == Guid.Empty)
            throw Unconfirmed();
        var variation = VariationId(row);
        var name = VariationName(row);
        ValidateVariation(variation, name);
        return new() { ProviderItemId = itemId, ProductId = product, VariationId = variation, VariationName = name };
    }

    private static Guid? VariationId(JsonElement row)
    {
        if (!row.TryGetProperty("variationId", out var value)) throw Unconfirmed();
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out var id) && id != Guid.Empty)
            return id;
        throw Unconfirmed();
    }

    private static string? VariationName(JsonElement row)
    {
        if (!row.TryGetProperty("variationName", out var value)) throw Unconfirmed();
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        throw Unconfirmed();
    }

    private static void ValidateVariation(Guid? variation, string? name)
    {
        if (variation.HasValue != (name is not null)) throw Unconfirmed();
        if (name is not null && (string.IsNullOrWhiteSpace(name)
            || name.Length > TenantCatalogueLimits.VariationNameLength || name.Any(char.IsControl))) throw Unconfirmed();
    }

    private static ChannelConsoleException Unconfirmed() => new(409, "The marketplace mapping is unconfirmed. Review menu publication before processing new orders.");
}
