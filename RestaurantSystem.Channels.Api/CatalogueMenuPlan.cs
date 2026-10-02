using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestaurantSystem.Channels.Api;

public sealed record CatalogueMenuPlan(string MappingHash, string SourceRevision, string Revision, JsonElement Menu,
    IReadOnlyList<CatalogueMenuSelection> Items)
{
    public bool CanPublish => Items.All(row => row.BlockReason.Length == 0);
}
public sealed record CatalogueMenuSelection(string ItemId, Guid ProductId, Guid? VariationId, string Name,
    string? VariationName, int? PriceMinor, bool Available, string BlockReason);

public static class CatalogueMenuPlanner
{
    public static string MappingHash(TenantStoreBinding store) => ProviderJson.Hash(ProviderJson.Encode(new
    {
        store.StoreId,
        store.TenantId,
        store.BaseUrl,
        store.Currency,
        store.CatalogueRevision,
        Items = store.Items.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal).Select(row => new
        { row.ProviderItemId, row.ProductId, row.VariationId, row.VariationName })
    }));

    public static CatalogueMenuPlan Build(JsonElement template, TenantStoreBinding store, TenantCatalogueSnapshot source)
    {
        var mappingHash = MappingHash(store);
        var sourceItems = source.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        var items = store.Items.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal).Select(mapping =>
        {
            if (!sourceItems.TryGetValue((mapping.ProductId, mapping.VariationId), out var item)) throw Invalid();
            var reason = item.BlockReason;
            if (reason.Length == 0 && item.VariationName != mapping.VariationName) reason = "MappingNameMismatch";
            return new CatalogueMenuSelection(mapping.ProviderItemId, mapping.ProductId, mapping.VariationId,
                item.Name, item.VariationName, item.PriceMinor, item.Available, reason);
        }).ToArray();
        if (items.Any(row => row.BlockReason.Length > 0)) return new(mappingHash, source.Revision, "", default, items);
        RequireTemplate(template, items);
        var menu = JsonNode.Parse(template.GetRawText())!;
        foreach (var item in items)
        {
            var row = menu["items"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == item.ItemId)!;
            row["title"] = JsonSerializer.SerializeToNode(new { translations = new { en_us = item.Name } });
            row["description"] = JsonSerializer.SerializeToNode(new { translations = new { en_us = item.Description(source) } });
            row["price_info"] = JsonSerializer.SerializeToNode(new { price = item.PriceMinor });
            row["suspension_info"] = JsonSerializer.SerializeToNode(new
            {
                suspension = item.Available ? null : new { suspend_until = UberAvailabilityClient.MaximumSuspensionTimestamp, reason = "Unavailable in Sofra" },
                overrides = Array.Empty<object>()
            });
        }
        var body = JsonSerializer.SerializeToElement(menu);
        var revision = ProviderJson.Hash(ProviderJson.Encode(new { mappingHash, sourceRevision = source.Revision, menu = body }));
        return new(mappingHash, source.Revision, revision, body, items);
    }

    private static string Description(this CatalogueMenuSelection item, TenantCatalogueSnapshot source)
        => source.Items.Single(row => row.ProductId == item.ProductId && row.VariationId == item.VariationId).Description;

    private static void RequireTemplate(JsonElement template, CatalogueMenuSelection[] items)
    {
        if (template.ValueKind != JsonValueKind.Object || !template.TryGetProperty("items", out var rows)
            || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != items.Length
            || !template.TryGetProperty("modifier_groups", out var groups) || groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() != 0)
            throw Invalid();
        var ids = items.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            if (!ids.Remove(ProviderJson.Text(row, "id")) || !row.TryGetProperty("tax_info", out var tax)
                || tax.ValueKind != JsonValueKind.Object
                || !tax.TryGetProperty("vat_rate_percentage", out var rate) || rate.ValueKind != JsonValueKind.Number
                || !rate.TryGetDecimal(out var percentage) || percentage < 0 || percentage > TenantCatalogueLimits.MaximumVatPercentage
                || row.TryGetProperty("modifier_group_ids", out _)) throw Invalid();
        }
    }

    public static JsonElement Structural(JsonElement menu)
    {
        var value = JsonNode.Parse(menu.GetRawText())!;
        foreach (var row in value["items"]!.AsArray()) row!.AsObject().Remove("suspension_info");
        return JsonSerializer.SerializeToElement(value);
    }

    private static ChannelConsoleException Invalid() => new(409, "The reviewed menu template does not match its tenant item selection and tax settings.");
}
