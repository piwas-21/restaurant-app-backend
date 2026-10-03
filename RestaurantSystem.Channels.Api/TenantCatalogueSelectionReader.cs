using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

internal sealed class TenantCatalogueSelectionReader(ITenantChannelTransport transport)
{
    public async Task<TenantCatalogueCategories> Categories(TenantStoreBinding store, string expectedSourceRevision,
        IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemReference> itemReferences,
        IReadOnlyList<TenantCatalogueItemOverride> overrides,
        CancellationToken cancellationToken)
    {
        RequireStore(store);
        if (categoryIds is null || itemReferences is null || overrides is null || expectedSourceRevision is null)
            throw InvalidInput();
        if (categoryIds.Count > TenantCatalogueLimits.MaximumCategoryReferences) throw CategoryLimit();
        if (overrides.Count > TenantCatalogueLimits.MaximumItemOverrides) throw OverrideLimit();
        if (itemReferences.Count > TenantCatalogueLimits.SelectionCount
            || !OptionalRevision(expectedSourceRevision) || categoryIds.Any(id => id == Guid.Empty)
            || categoryIds.Distinct().Count() != categoryIds.Count
            || itemReferences.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty || row.CategoryId is null)
            || itemReferences.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != itemReferences.Count
            || overrides.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty || row.CategoryId is null)
            || overrides.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != overrides.Count
            || itemReferences.Concat(overrides.Select(row => new TenantCatalogueItemReference(
                row.ProductId, row.VariationId, row.CategoryId)))
                .GroupBy(row => (row.ProductId, row.VariationId)).Any(group => group.Select(row => row.CategoryId).Distinct().Count() > 1))
            throw InvalidInput();
        var request = new
        {
            expectedSourceRevision,
            categoryIds,
            itemReferences,
            itemOverrides = overrides.Select(row => new
            { row.ProductId, row.VariationId, row.CategoryId, row.Selected })
        };
        var reply = await transport.Post(TokenBinding(store), "/api/delivery-channels/catalogue/categories/compare-snapshot", request, cancellationToken);
        if (reply is not { ValueKind: JsonValueKind.Object } body) throw Invalid();
        RequireBinding(body, store);
        if (ProviderJson.Text(body, "categoryBasis") != "primaryCategory") throw Invalid();
        var maximumCategoryCount = Integer(body, "maximumCategoryCount");
        var maximumOverrideCount = Integer(body, "maximumItemOverrideCount");
        if (maximumCategoryCount != TenantCatalogueLimits.MaximumCategoryCount
            || maximumOverrideCount != TenantCatalogueLimits.MaximumItemOverrides) throw Invalid();
        var revision = Revision(body);
        var language = ProviderJson.Text(body, "language");
        var categories = ReadCategories(Property(body, "categories"));
        var sourceChanged = Flag(body, "sourceChanged");
        if (sourceChanged != (expectedSourceRevision.Length > 0 && expectedSourceRevision != revision)) throw Invalid();
        return new(revision, language, categories)
        {
            CategoryBasis = "primaryCategory",
            MaximumCategoryCount = maximumCategoryCount,
            MaximumItemOverrideCount = maximumOverrideCount,
            SourceChanged = sourceChanged,
            RemovedCategoryIds = ReadGuidArray(Property(body, "removedCategoryIds")),
            RemovedItems = ReadRemovedItems(Property(body, "removedItems")),
            RemovedItemOverrides = ReadRemovedOverrides(Property(body, "removedItemOverrides")),
            ItemStatuses = ReadItemStatuses(Property(body, "itemStatuses"), itemReferences, overrides)
        };
    }

    public async Task<TenantCatalogueSelection> ReadSelection(TenantStoreBinding store, string expectedSourceRevision,
        IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemOverride> overrides,
        CancellationToken cancellationToken)
    {
        RequireStore(store);
        if (categoryIds is null || overrides is null || expectedSourceRevision is null
            || expectedSourceRevision.Length != TenantCatalogueLimits.RevisionLength
            || expectedSourceRevision.Any(c => !char.IsAsciiHexDigitLower(c))) throw InvalidInput();
        if (categoryIds.Count > TenantCatalogueLimits.MaximumCategoryReferences) throw CategoryLimit();
        if (overrides.Count > TenantCatalogueLimits.MaximumItemOverrides) throw OverrideLimit();
        if (categoryIds.Any(id => id == Guid.Empty) || categoryIds.Distinct().Count() != categoryIds.Count
            || overrides.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty || row.CategoryId is null)
            || overrides.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != overrides.Count)
            throw InvalidInput();
        var request = new
        {
            expectedSourceRevision,
            categoryIds,
            itemOverrides = overrides.Select(row => new
            { row.ProductId, row.VariationId, row.CategoryId, row.Selected })
        };
        var reply = await transport.Post(TokenBinding(store), "/api/delivery-channels/catalogue/selection-snapshot", request, cancellationToken);
        if (reply is not { ValueKind: JsonValueKind.Object } body) throw Invalid();
        RequireBinding(body, store);
        var revision = Revision(body);
        if (revision != expectedSourceRevision) throw Invalid();
        var language = ProviderJson.Text(body, "language");
        var categories = ReadSelectionCategories(Property(body, "categories"));
        var selectedCategories = ReadGuidArray(Property(body, "selectedCategoryIds"));
        var itemOverrides = ReadOverrides(Property(body, "itemOverrides"));
        var items = ReadItems(Property(body, "items"));
        if (items.Length is < 1 or > TenantCatalogueLimits.SelectionCount
            || items.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != items.Length)
            throw Invalid();
        return new(revision, language, categories, selectedCategories, itemOverrides, items);
    }

    private static TenantCatalogueCategory[] ReadCategories(JsonElement rows)
    {
        var categories = ReadArray(rows, row => new TenantCatalogueCategory(GuidValue(row, "categoryId"),
            RequiredText(row, "name", 200), Integer(row, "displayOrder"), Integer(row, "totalItemCount"),
            Integer(row, "supportedItemCount"), Integer(row, "unsupportedItemCount"), Flag(row, "active")));
        if (categories.Length > TenantCatalogueLimits.MaximumCategoryCount) throw Invalid();
        return categories;
    }

    private static TenantCatalogueSelectionCategory[] ReadSelectionCategories(JsonElement rows)
        => ReadArray(rows, row => new TenantCatalogueSelectionCategory(GuidValue(row, "categoryId"),
            RequiredText(row, "name", 200), Integer(row, "displayOrder"), Integer(row, "totalItemCount"),
            Integer(row, "supportedItemCount"), Integer(row, "unsupportedItemCount"), Integer(row, "selectedItemCount"),
            Integer(row, "selectedUnsupportedItemCount"), Flag(row, "active")));

    private static TenantCatalogueSelectionItem[] ReadItems(JsonElement rows)
        => ReadArray(rows, row =>
        {
            var product = GuidValue(row, "productId");
            var variation = OptionalGuid(row, "variationId");
            var category = OptionalGuid(row, "categoryId");
            var selectionKey = RequiredText(row, "selectionKey", 80);
            if (selectionKey != SelectionKey(product, variation)) throw Invalid();
            var categoryName = OptionalText(row, "categoryName", 200);
            var categoryOrder = OptionalInteger(row, "categoryDisplayOrder");
            var price = OptionalInteger(row, "priceMinor");
            var reason = Text(row, "blockReason", 80, allowEmpty: true);
            var supported = Flag(row, "supported");
            var available = Flag(row, "available");
            var name = RequiredText(row, "name", TenantCatalogueLimits.ItemNameLength);
            var description = Text(row, "description", TenantCatalogueLimits.DescriptionLength, allowEmpty: true);
            var variationName = OptionalText(row, "variationName", TenantCatalogueLimits.VariationNameLength);
            var fingerprint = RequiredText(row, "sourceFingerprint", TenantCatalogueLimits.RevisionLength);
            if (fingerprint.Length != TenantCatalogueLimits.RevisionLength || fingerprint.Any(c => !char.IsAsciiHexDigitLower(c))
                || supported != (reason.Length == 0) || category.HasValue != (categoryName is not null)
                || category.HasValue != categoryOrder.HasValue || (reason.Length == 0 && price is null)) throw Invalid();
            return new TenantCatalogueSelectionItem(selectionKey, product, variation, category, categoryName,
                categoryOrder, Integer(row, "itemDisplayOrder"), name, description, variationName, price, available,
                supported, reason, fingerprint);
        });

    private static TenantCatalogueItemOverride[] ReadOverrides(JsonElement rows)
        => ReadArray(rows, row =>
        {
            var product = GuidValue(row, "productId");
            var variation = OptionalGuid(row, "variationId");
            var category = OptionalGuid(row, "categoryId");
            var key = RequiredText(row, "selectionKey", 80);
            var fingerprint = RequiredText(row, "sourceFingerprint", TenantCatalogueLimits.RevisionLength);
            if (key != SelectionKey(product, variation) || fingerprint.Length != TenantCatalogueLimits.RevisionLength
                || fingerprint.Any(c => !char.IsAsciiHexDigitLower(c)) || category is null) throw Invalid();
            return new TenantCatalogueItemOverride(product, variation, category, Flag(row, "selected"))
            {
                SelectionKey = key,
                SourceFingerprint = fingerprint,
                Supported = Flag(row, "supported")
            };
        });

    private static TenantCatalogueRemovedItemOverride[] ReadRemovedOverrides(JsonElement rows)
        => ReadArray(rows, row =>
        {
            var product = GuidValue(row, "productId");
            var variation = OptionalGuid(row, "variationId");
            var category = GuidValue(row, "categoryId");
            var currentCategory = OptionalGuid(row, "currentCategoryId");
            var selectionKey = RequiredText(row, "selectionKey", 80);
            var reason = RequiredText(row, "reason", 32);
            if (selectionKey != SelectionKey(product, variation)
                || reason is not ("itemRemoved" or "categoryRemoved" or "categoryChanged")) throw Invalid();
            return new TenantCatalogueRemovedItemOverride(selectionKey, product, variation, category, currentCategory, reason);
        });

    private static TenantCatalogueRemovedItemReference[] ReadRemovedItems(JsonElement rows)
        => ReadArray(rows, row =>
        {
            var product = GuidValue(row, "productId");
            var variation = OptionalGuid(row, "variationId");
            var category = GuidValue(row, "categoryId");
            var currentCategory = OptionalGuid(row, "currentCategoryId");
            var selectionKey = RequiredText(row, "selectionKey", 80);
            var reason = RequiredText(row, "reason", 32);
            if (selectionKey != SelectionKey(product, variation)
                || reason is not ("itemRemoved" or "categoryRemoved" or "categoryChanged")) throw Invalid();
            return new TenantCatalogueRemovedItemReference(selectionKey, product, variation, category, currentCategory, reason);
        });

    private static TenantCatalogueItemStatus[] ReadItemStatuses(JsonElement rows,
        IReadOnlyList<TenantCatalogueItemReference> references, IReadOnlyList<TenantCatalogueItemOverride> overrides)
    {
        var requested = references.Concat(overrides.Select(row => new TenantCatalogueItemReference(
            row.ProductId, row.VariationId, row.CategoryId)))
            .GroupBy(row => (row.ProductId, row.VariationId)).ToDictionary(group => group.Key, group => group.First());
        var statuses = ReadArray(rows, row =>
        {
            var product = GuidValue(row, "productId");
            var variation = OptionalGuid(row, "variationId");
            var category = GuidValue(row, "categoryId");
            var currentCategory = OptionalGuid(row, "currentCategoryId");
            var key = RequiredText(row, "selectionKey", 80);
            if (key != SelectionKey(product, variation)
                || !requested.TryGetValue((product, variation), out var requestedRow)
                || requestedRow.CategoryId != category) throw Invalid();
            return new TenantCatalogueItemStatus(key, product, variation, category, currentCategory,
                Flag(row, "supported"));
        });
        if (statuses.Length > TenantCatalogueLimits.SelectionCount + TenantCatalogueLimits.MaximumItemOverrides
            || statuses.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != statuses.Length) throw Invalid();
        return statuses;
    }

    private static Guid[] ReadGuidArray(JsonElement rows)
    {
        if (rows.ValueKind != JsonValueKind.Array) throw Invalid();
        var values = rows.EnumerateArray().Select(row =>
        {
            if (row.ValueKind != JsonValueKind.String || !Guid.TryParseExact(row.GetString(), "D", out var id) || id == Guid.Empty) throw Invalid();
            return id;
        }).ToArray();
        if (values.Distinct().Count() != values.Length) throw Invalid();
        return values;
    }

    private static T[] ReadArray<T>(JsonElement rows, Func<JsonElement, T> map)
    {
        if (rows.ValueKind != JsonValueKind.Array) throw Invalid();
        return rows.EnumerateArray().Select(map).ToArray();
    }

    private static void RequireStore(TenantStoreBinding store)
    {
        if (store.CatalogueApiToken.Length == 0 || store.StoreId == Guid.Empty
            || store.Currency is not ("EUR" or "CHF")) throw Invalid();
    }

    private static TenantStoreBinding TokenBinding(TenantStoreBinding store)
        => new() { BaseUrl = store.BaseUrl, ApiToken = store.CatalogueApiToken };

    private static void RequireBinding(JsonElement body, TenantStoreBinding store)
    {
        if (ProviderJson.Text(body, "provider") != "uber-eats" || ProviderJson.Text(body, "storeId") != store.StoreId.ToString("D")
            || ProviderJson.Text(body, "currency") != store.Currency || !body.TryGetProperty("isSandbox", out var sandbox)
            || sandbox.ValueKind != JsonValueKind.True) throw Invalid();
    }

    private static string Revision(JsonElement body)
    {
        var revision = ProviderJson.Text(body, "revision");
        if (revision.Length != TenantCatalogueLimits.RevisionLength || revision.Any(c => !char.IsAsciiHexDigitLower(c))) throw Invalid();
        return revision;
    }

    private static JsonElement Property(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) ? value : throw Invalid();
    private static Guid GuidValue(JsonElement row, string name)
        => Guid.TryParseExact(ProviderJson.Text(row, name), "D", out var id) && id != Guid.Empty ? id : throw Invalid();
    private static Guid? OptionalGuid(JsonElement row, string name)
    {
        var value = Property(row, name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out var id) && id != Guid.Empty
            ? id : throw Invalid();
    }
    private static int Integer(JsonElement row, string name)
        => Property(row, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) && number >= 0 ? number : throw Invalid();
    private static int? OptionalInteger(JsonElement row, string name)
    {
        var value = Property(row, name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : throw Invalid();
    }
    private static bool Flag(JsonElement row, string name)
    {
        var value = Property(row, name);
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Invalid();
    }
    private static string RequiredText(JsonElement row, string name, int maximum)
        => Text(row, name, maximum, allowEmpty: false);
    private static string Text(JsonElement row, string name, int maximum, bool allowEmpty)
    {
        var value = ProviderJson.Text(row, name);
        if (!row.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String
            || (!allowEmpty && value.Length == 0) || value.Length > maximum || value.Any(char.IsControl)) throw Invalid();
        return value;
    }
    private static string? OptionalText(JsonElement row, string name, int maximum)
    {
        var value = Property(row, name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            && text.Length <= maximum && !text.Any(char.IsControl) ? text : throw Invalid();
    }
    private static string SelectionKey(Guid product, Guid? variation) => $"{product:D}:{variation?.ToString("D") ?? "base"}";
    private static bool OptionalRevision(string value)
        => value.Length == 0 || value.Length == TenantCatalogueLimits.RevisionLength && value.All(char.IsAsciiHexDigitLower);
    private static ChannelConsoleException Invalid() => new(502, "The tenant catalogue selection reply was malformed or outside its reviewed bounds.");
    private static ChannelConsoleException InvalidInput() => new(400, "Refresh the tenant catalogue and review the selected categories and products.");
    private static ChannelConsoleException CategoryLimit() => new(400, "Review no more than 1,000 categories at a time.", "CategoryLimitExceeded");
    private static ChannelConsoleException OverrideLimit() => new(400, "Review no more than 2,000 individual item overrides at a time.", "SelectionOverrideLimitExceeded");
}
