using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantCatalogueClient(ITenantChannelTransport transport) : ITenantCatalogueClient
{
    public Task<TenantCatalogueCategories> Categories(TenantStoreBinding store, string expectedSourceRevision,
        IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemReference> itemReferences,
        IReadOnlyList<TenantCatalogueItemOverride> overrides,
        CancellationToken cancellationToken)
        => new TenantCatalogueSelectionReader(transport).Categories(store, expectedSourceRevision,
            categoryIds, itemReferences, overrides, cancellationToken);

    public Task<TenantCatalogueSelection> ReadSelection(TenantStoreBinding store, string expectedSourceRevision,
        IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemOverride> overrides,
        CancellationToken cancellationToken)
        => new TenantCatalogueSelectionReader(transport).ReadSelection(store, expectedSourceRevision,
            categoryIds, overrides, cancellationToken);

    public async Task<TenantCatalogueSnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
    {
        if (store.Categories.Count > 0 || store.SourceRevision.Length > 0)
            return await ReadCategorySelection(store, cancellationToken);
        if (store.CatalogueApiToken.Length == 0 || store.Items.Count < 1 || store.Items.Count > TenantCatalogueLimits.SelectionCount) throw Invalid();
        var expected = store.Items.Select(row => (row.ProductId, row.VariationId)).ToHashSet();
        var response = await transport.Post(new() { BaseUrl = store.BaseUrl, ApiToken = store.CatalogueApiToken },
            "/api/delivery-channels/catalogue/snapshot", new
            {
                provider = "uber-eats",
                storeId = store.StoreId.ToString("D"),
                currency = store.Currency,
                isSandbox = true,
                language = "en",
                items = expected.Select(row => new { row.ProductId, row.VariationId })
            }, cancellationToken);
        if (response is not { ValueKind: JsonValueKind.Object } body || ProviderJson.Text(body, "provider") != "uber-eats"
            || ProviderJson.Text(body, "storeId") != store.StoreId.ToString("D") || ProviderJson.Text(body, "currency") != store.Currency
            || ProviderJson.Text(body, "language") != "en" || !body.TryGetProperty("isSandbox", out var sandbox) || sandbox.ValueKind != JsonValueKind.True
            || !body.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != expected.Count)
            throw Invalid();
        var revision = ProviderJson.Text(body, "revision");
        if (revision.Length != TenantCatalogueLimits.RevisionLength || revision.Any(c => !char.IsAsciiHexDigitLower(c))) throw Invalid();
        var seen = new HashSet<(Guid, Guid?)>(); var items = new List<TenantCatalogueItem>();
        foreach (var row in rows.EnumerateArray())
        {
            var item = Item(row);
            if (!expected.Contains((item.ProductId, item.VariationId)) || !seen.Add((item.ProductId, item.VariationId))) throw Invalid();
            items.Add(item);
        }
        var computed = ProviderJson.Hash(ProviderJson.Encode(new
        {
            Provider = "uber-eats",
            StoreId = store.StoreId.ToString("D"),
            store.Currency,
            IsSandbox = true,
            Language = "en",
            Items = items.OrderBy(item => item.ProductId).ThenBy(item => item.VariationId).ToArray()
        }));
        if (computed != revision) throw Invalid();
        return new(revision, items);
    }

    private async Task<TenantCatalogueSnapshot> ReadCategorySelection(TenantStoreBinding store,
        CancellationToken cancellationToken)
    {
        var selection = await ReadSelection(store, store.SourceRevision, store.SelectedCategoryIds,
            store.ItemOverrides.Select(row => new TenantCatalogueItemOverride(row.ProductId, row.VariationId,
                row.CategoryId, row.Selected)).ToArray(), cancellationToken);
        var byIdentity = selection.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        if (selection.Items.Count != store.Items.Count || selection.Language != store.Language
            || selection.SelectedCategoryIds.Except(store.SelectedCategoryIds).Any()
            || store.SelectedCategoryIds.Except(selection.SelectedCategoryIds).Any()
            || !SameOverrides(store.ItemOverrides, selection.ItemOverrides)) throw Invalid();
        var mapped = store.Items.Select(mapping =>
        {
            if (!byIdentity.TryGetValue((mapping.ProductId, mapping.VariationId), out var row)
                || row.SelectionKey != mapping.SelectionKey || row.SourceFingerprint != mapping.SourceFingerprint
                || row.CategoryId != mapping.CategoryId) throw Invalid();
            return new TenantCatalogueItem(row.ProductId, row.VariationId, row.Name, row.Description, row.VariationName,
                row.PriceMinor, row.Available, row.BlockReason)
            {
                SelectionKey = row.SelectionKey,
                CategoryId = row.CategoryId,
                CategoryName = row.CategoryName,
                CategoryDisplayOrder = row.CategoryDisplayOrder,
                ItemDisplayOrder = row.ItemDisplayOrder,
                SourceFingerprint = row.SourceFingerprint
            };
        }).ToArray();
        return new(selection.Revision, mapped) { Language = selection.Language, Categories = selection.Categories };
    }

    private static bool SameOverrides(List<TenantItemMappingOverride> expected,
        IReadOnlyList<TenantCatalogueItemOverride> actual)
    {
        if (expected.Count != actual.Count) return false;
        var rows = actual.ToDictionary(row => (row.ProductId, row.VariationId));
        return expected.All(row => rows.TryGetValue((row.ProductId, row.VariationId), out var current)
            && current.CategoryId == row.CategoryId && current.Selected == row.Selected
            && current.SelectionKey == row.SelectionKey && current.SourceFingerprint == row.SourceFingerprint
            && current.Supported == row.Supported);
    }

    private static TenantCatalogueItem Item(JsonElement row)
    {
        if (!Guid.TryParseExact(ProviderJson.Text(row, "productId"), "D", out var product) || product == Guid.Empty
            || !row.TryGetProperty("variationId", out var variationValue) || !row.TryGetProperty("variationName", out var variationNameValue)
            || !row.TryGetProperty("available", out var available) || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !row.TryGetProperty("priceMinor", out var priceValue)
            || !row.TryGetProperty("blockReason", out var reasonValue) || reasonValue.ValueKind != JsonValueKind.String
            || !row.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String
            || !row.TryGetProperty("description", out var descriptionValue) || descriptionValue.ValueKind != JsonValueKind.String) throw Invalid();
        var (variation, variationName) = Variation(variationValue, variationNameValue);
        var price = Price(priceValue);
        var reason = ProviderJson.Text(row, "blockReason");
        var name = ProviderJson.Text(row, "name"); var description = ProviderJson.Text(row, "description");
        RequireContent(reason, name, description, variation, variationName, price, available.GetBoolean());
        return new(product, variation, name, description, variationName, price, available.GetBoolean(), reason);
    }

    private static (Guid? Id, string? Name) Variation(JsonElement variationValue, JsonElement variationNameValue)
    {
        Guid? variation = null;
        if (variationValue.ValueKind != JsonValueKind.Null)
        {
            if (variationValue.ValueKind != JsonValueKind.String || !Guid.TryParseExact(variationValue.GetString(), "D", out var id) || id == Guid.Empty) throw Invalid();
            variation = id;
        }
        string? variationName = null;
        if (variationNameValue.ValueKind != JsonValueKind.Null)
        {
            if (variationNameValue.ValueKind != JsonValueKind.String) throw Invalid();
            variationName = variationNameValue.GetString();
        }
        return (variation, variationName);
    }

    private static int? Price(JsonElement priceValue)
    {
        int? price = null;
        if (priceValue.ValueKind != JsonValueKind.Null)
        {
            if (priceValue.ValueKind != JsonValueKind.Number || !priceValue.TryGetInt32(out var value) || value < 0) throw Invalid();
            price = value;
        }
        return price;
    }

    private static void RequireContent(string reason, string name, string description, Guid? variation,
        string? variationName, int? price, bool available)
    {
        if (reason is not ("" or "MissingProduct" or "UnavailableProduct" or "DeliveryDisabled" or "UnsupportedChoices"
            or "UnmappedAllergens" or "MissingTranslation" or "UnavailableVariation" or "VariationRequired" or "InvalidText" or "InvalidPrice")) throw Invalid();
        if (reason.Length == 0 && (price is null || string.IsNullOrWhiteSpace(name) || name.Length > TenantCatalogueLimits.ItemNameLength || name.Any(char.IsControl)
            || description.Length > TenantCatalogueLimits.DescriptionLength || variation.HasValue != (variationName is not null)
            || variationName is not null && (string.IsNullOrWhiteSpace(variationName) || variationName.Length > TenantCatalogueLimits.VariationNameLength || variationName.Any(char.IsControl)))) throw Invalid();
        if (reason.Length > 0 && (price is not null || available)) throw Invalid();
    }

    private static ChannelConsoleException Invalid() => new(502, "The tenant catalogue snapshot is malformed or outside its reviewed selection.");
}
