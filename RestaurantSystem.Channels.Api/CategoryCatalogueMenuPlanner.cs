using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestaurantSystem.Channels.Api;

internal static class CategoryCatalogueMenuPlanner
{
    private const string UnavailableTax = "ReviewedTaxProfileUnavailable";

    public static CatalogueMenuPlan Build(JsonElement template, TenantStoreBinding store, TenantCatalogueSnapshot source)
    {
        var mappingHash = CatalogueMenuPlanner.MappingHash(store);
        var sourceItems = source.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        var selections = store.Items.OrderBy(row => row.CategoryDisplayOrder ?? int.MaxValue)
            .ThenBy(row => row.CategoryId).ThenBy(row => row.ItemDisplayOrder)
            .ThenBy(row => row.ProductId).ThenBy(row => row.VariationId)
            .Select(mapping => Map(mapping, sourceItems)).ToArray();
        var categories = CategoryRows(store, source, selections);
        if (source.Revision != store.SourceRevision || source.Language != store.Language)
            selections = selections.Select(row => row with { BlockReason = "SourceRevisionChanged" }).ToArray();
        if (categories.Length == 0 || categories.Length != selections.Where(row => row.CategoryId.HasValue)
                .Select(row => row.CategoryId).Distinct().Count())
            selections = selections.Select(row => row with
            {
                BlockReason = row.BlockReason.Length == 0
                ? "CategorySnapshotMismatch" : row.BlockReason
            }).ToArray();
        if (!TryTaxProfile(template, out var taxInfo, out var taxRevision, out var rate)
            || !CompatibleTemplates(template))
            selections = selections.Select(row => row with { BlockReason = row.BlockReason.Length == 0 ? UnavailableTax : row.BlockReason }).ToArray();
        if (selections.Length == 0)
            return new(mappingHash, source.Revision, string.Empty, default, selections, categories,
                taxInfo, taxRevision, rate, true);
        if (selections.Any(row => row.BlockReason.Length > 0))
            return new(mappingHash, source.Revision, string.Empty, default, selections, categories,
                taxInfo, taxRevision, rate, true);
        var menu = BuildMenu(template, store, selections, categories, taxInfo);
        var revision = ProviderJson.Hash(ProviderJson.Encode(new { mappingHash, sourceRevision = source.Revision, menu }));
        return new(mappingHash, source.Revision, revision, menu, selections, categories,
            taxInfo, taxRevision, rate, true);
    }

    private static CatalogueMenuSelection Map(TenantItemMapping mapping,
        Dictionary<(Guid ProductId, Guid? VariationId), TenantCatalogueItem> sourceItems)
    {
        if (!sourceItems.TryGetValue((mapping.ProductId, mapping.VariationId), out var item))
            return Block(mapping, "SelectionSnapshotMismatch");
        var reason = item.BlockReason;
        if (item.SelectionKey != mapping.SelectionKey || item.SourceFingerprint != mapping.SourceFingerprint
            || item.CategoryId != mapping.CategoryId || item.CategoryDisplayOrder != mapping.CategoryDisplayOrder
            || item.ItemDisplayOrder != mapping.ItemDisplayOrder || item.CategoryName != mapping.CategoryName
            || item.VariationName != mapping.VariationName) reason = "SelectionSnapshotMismatch";
        return new(mapping.ProviderItemId, mapping.ProductId, mapping.VariationId, item.Name,
            item.VariationName, item.PriceMinor, item.Available, reason, item.SelectionKey, item.CategoryId,
            item.CategoryName, item.CategoryDisplayOrder, item.ItemDisplayOrder, item.Description, item.SourceFingerprint);
    }

    private static CatalogueMenuSelection Block(TenantItemMapping mapping, string reason)
        => new(mapping.ProviderItemId, mapping.ProductId, mapping.VariationId, string.Empty, mapping.VariationName,
            null, false, reason, mapping.SelectionKey, mapping.CategoryId, mapping.CategoryName,
            mapping.CategoryDisplayOrder, mapping.ItemDisplayOrder, string.Empty, mapping.SourceFingerprint);

    private static CatalogueMenuCategory[] CategoryRows(TenantStoreBinding store, TenantCatalogueSnapshot source,
        IReadOnlyList<CatalogueMenuSelection> items)
    {
        var summaries = source.Categories.ToDictionary(row => row.CategoryId);
        var selected = items.Where(row => row.CategoryId.HasValue).GroupBy(row => row.CategoryId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (items.Any(row => !row.CategoryId.HasValue) || selected.Keys.Any(id => !summaries.ContainsKey(id)))
            return [];
        var categories = new List<CatalogueMenuCategory>();
        foreach (var row in store.Categories.OrderBy(row => row.DisplayOrder).ThenBy(row => row.CategoryId))
        {
            if (!summaries.TryGetValue(row.CategoryId, out var sourceCategory)) return [];
            var rows = selected.GetValueOrDefault(row.CategoryId) ?? [];
            if (sourceCategory.Name != row.Name || sourceCategory.DisplayOrder != row.DisplayOrder || sourceCategory.Active != row.Active
                || sourceCategory.TotalItemCount != row.TotalItemCount
                || sourceCategory.SupportedItemCount != row.SupportedItemCount
                || sourceCategory.UnsupportedItemCount != row.UnsupportedItemCount)
                return [];
            categories.Add(new CatalogueMenuCategory(row.ProviderCategoryId, row.CategoryId, row.Name, row.DisplayOrder,
                sourceCategory.TotalItemCount, sourceCategory.SupportedItemCount, sourceCategory.UnsupportedItemCount,
                rows.Length, rows.Count(item => item.BlockReason.Length > 0)));
        }
        var expected = selected.Keys.ToHashSet();
        if (!expected.SetEquals(categories.Where(row => row.SelectedItemCount > 0).Select(row => row.TenantCategoryId))) return [];
        return categories.Where(row => row.SelectedItemCount > 0).ToArray();
    }

    private static bool TryTaxProfile(JsonElement template, out JsonElement taxInfo,
        out string revision, out decimal vatRate)
    {
        taxInfo = default; revision = string.Empty; vatRate = 0;
        if (!template.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return false;
        var rows = items.EnumerateArray().ToArray();
        var first = rows[0];
        if (!first.TryGetProperty("tax_info", out taxInfo) || taxInfo.ValueKind != JsonValueKind.Object
            || !taxInfo.TryGetProperty("vat_rate_percentage", out var rate) || rate.ValueKind != JsonValueKind.Number
            || !rate.TryGetDecimal(out vatRate) || vatRate is < 0 or > TenantCatalogueLimits.MaximumVatPercentage) return false;
        var selectedTaxInfo = taxInfo;
        if (rows.Skip(1).Any(row => !row.TryGetProperty("tax_info", out var other)
            || !JsonElement.DeepEquals(selectedTaxInfo, other))) return false;
        revision = ProviderJson.Hash(taxInfo);
        return true;
    }

    private static bool CompatibleTemplates(JsonElement template)
    {
        if (!template.TryGetProperty("modifier_groups", out var groups) || groups.ValueKind != JsonValueKind.Array
            || groups.GetArrayLength() != 0 || !template.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0
            || !template.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array
            || categories.GetArrayLength() == 0) return false;
        return SameStatic(items, new HashSet<string>(["id", "title", "description", "price_info", "tax_info", "suspension_info"], StringComparer.Ordinal))
            && SameStatic(categories, new HashSet<string>(["id", "title", "entities"], StringComparer.Ordinal));
    }

    private static bool SameStatic(JsonElement rows, IReadOnlySet<string> dynamicProperties)
    {
        var baseline = Without(rows[0], dynamicProperties);
        return rows.EnumerateArray().Skip(1).All(row => JsonElement.DeepEquals(baseline, Without(row, dynamicProperties)));
    }

    private static JsonElement Without(JsonElement value, IReadOnlySet<string> properties)
    {
        var node = JsonNode.Parse(value.GetRawText())!.AsObject();
        foreach (var property in properties) node.Remove(property);
        return JsonSerializer.SerializeToElement(node);
    }

    private static JsonElement BuildMenu(JsonElement template, TenantStoreBinding store,
        IReadOnlyList<CatalogueMenuSelection> items, IReadOnlyList<CatalogueMenuCategory> categories,
        JsonElement taxInfo)
    {
        var menu = JsonNode.Parse(template.GetRawText())!.AsObject();
        var locale = UberLocale(store.Language);
        menu["items"] = ItemRows(template, items, taxInfo, locale);
        menu["categories"] = CategoryRows(template, categories, items, locale);
        if (menu["menus"] is not JsonArray menus || menus.Count == 0) throw Invalid();
        var categoryIds = categories.Select(row => row.CategoryId).ToArray();
        foreach (var dayMenu in menus)
        {
            if (dayMenu is not JsonObject objectMenu || objectMenu["category_ids"] is not JsonArray)
                throw Invalid();
            objectMenu["category_ids"] = JsonSerializer.SerializeToNode(categoryIds);
        }
        return JsonSerializer.SerializeToElement(menu);
    }

    private static JsonArray ItemRows(JsonElement template, IReadOnlyList<CatalogueMenuSelection> items,
        JsonElement taxInfo, string locale)
    {
        var baseRow = template.GetProperty("items")[0];
        return new JsonArray(items.Select(item =>
        {
            var row = JsonNode.Parse(baseRow.GetRawText())!.AsObject();
            row["id"] = item.ItemId;
            row["title"] = Translated(item.Name, locale);
            row["description"] = Translated(item.Description, locale);
            if (row["price_info"] is not JsonObject priceInfo || item.PriceMinor is null) throw Invalid();
            priceInfo["price"] = item.PriceMinor.Value;
            row["tax_info"] = JsonNode.Parse(taxInfo.GetRawText());
            row["suspension_info"] = Suspension(item.Available);
            return (JsonNode?)row;
        }).ToArray());
    }

    private static JsonArray CategoryRows(JsonElement template, IReadOnlyList<CatalogueMenuCategory> categories,
        IReadOnlyList<CatalogueMenuSelection> items, string locale)
    {
        var baseCategory = template.GetProperty("categories")[0];
        return new JsonArray(categories.Select(category =>
        {
            var row = JsonNode.Parse(baseCategory.GetRawText())!.AsObject();
            row["id"] = category.CategoryId;
            row["title"] = Translated(category.Name, locale);
            row["entities"] = JsonSerializer.SerializeToNode(items.Where(item => item.CategoryId == category.TenantCategoryId)
                .OrderBy(item => item.ItemDisplayOrder).ThenBy(item => item.ProductId).ThenBy(item => item.VariationId)
                .Select(item => new { id = item.ItemId, type = "ITEM" }).ToArray());
            return (JsonNode?)row;
        }).ToArray());
    }

    private static JsonObject Translated(string value, string locale)
        => new() { ["translations"] = new JsonObject { [locale] = value } };

    private static JsonObject Suspension(bool available)
        => new()
        {
            ["suspension"] = available ? null : JsonSerializer.SerializeToNode(new
            { suspend_until = UberAvailabilityClient.MaximumSuspensionTimestamp, reason = "Unavailable in Sofra" }),
            ["overrides"] = new JsonArray()
        };

    private static string UberLocale(string language) => language switch
    {
        "en" => "en_us",
        "nl" => "nl_nl",
        "fr" => "fr_fr",
        "de" => "de_de",
        "tr" => "tr_tr",
        "ar" => "ar_sa",
        _ => throw Invalid()
    };

    private static ChannelConsoleException Invalid()
        => new(409, "The reviewed menu template cannot safely publish this category selection.");
}
