using System.Text.Json;
using System.Net;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelManagementOperations
{
    private object[] Rows(TenantStoreBinding store, TenantCatalogueSnapshot source,
        JsonElement preview, JsonElement providerMenu, JsonElement template, string providerPriceStatus)
    {
        var planItems = preview.ValueKind == JsonValueKind.Object
            && preview.TryGetProperty("items", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().ToDictionary(row => ProviderJson.Text(row, "itemId"), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        return store.Items.Select(mapping =>
        {
            var item = source.Items.SingleOrDefault(row => row.ProductId == mapping.ProductId && row.VariationId == mapping.VariationId);
            planItems.TryGetValue(mapping.ProviderItemId, out var plan);
            var provider = FindProviderItem(providerMenu, mapping.ProviderItemId);
            var providerPrice = ProviderPrice(provider);
            if (provider.ValueKind != JsonValueKind.Object) provider = FindProviderItem(template, mapping.ProviderItemId);
            var priceIsKnown = providerPriceStatus != "unknown" && providerPrice is not null;
            var price = item?.PriceMinor;
            var blockReason = item?.BlockReason ?? "UnmappedProduct";
            if (blockReason.Length == 0 && plan.ValueKind == JsonValueKind.Object)
                blockReason = ProviderJson.Text(plan, "blockReason");
            return (object)new
            {
                providerItemId = mapping.ProviderItemId,
                providerItemName = ProviderTitle(provider),
                productId = mapping.ProductId,
                variationId = mapping.VariationId,
                productName = item?.Name,
                variationName = item?.VariationName,
                tenantPriceMinor = price,
                providerPriceMinor = priceIsKnown ? providerPrice : null,
                providerPriceStatus = priceIsKnown ? providerPriceStatus : "unknown",
                currency = store.Currency,
                available = item?.Available ?? false,
                mappingStatus = item is null ? "unmapped" : blockReason.Length > 0 ? "blocked" : "mapped",
                blockReason = blockReason.Length > 0 ? blockReason : null
            };
        }).ToArray();
    }

    private static string ProviderTitle(JsonElement item)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("title", out var title) && title.TryGetProperty("translations", out var translations)
            ? ProviderJson.Text(translations, "en_us") : string.Empty;
    private static int? ProviderPrice(JsonElement item)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("price_info", out var price)
            && price.ValueKind == JsonValueKind.Object && price.TryGetProperty("price", out var amount)
            && amount.ValueKind == JsonValueKind.Number && amount.TryGetInt32(out var value) ? value : null;

    private static string[] Blocks(JsonElement preview)
        => preview.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(item => ProviderJson.Text(item, "blockReason")).Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal).ToArray() : ["PreviewUnavailable"];

    private static object[] ServiceHours(JsonElement template)
    {
        if (template.ValueKind != JsonValueKind.Object || !template.TryGetProperty("menus", out var menus) || menus.ValueKind != JsonValueKind.Array || menus.GetArrayLength() == 0
            || !menus[0].TryGetProperty("service_availability", out var days) || days.ValueKind != JsonValueKind.Array) return [];
        return days.EnumerateArray().Select(day => (object)new
        {
            dayOfWeek = ProviderJson.Text(day, "day_of_week"),
            timePeriods = day.TryGetProperty("time_periods", out var periods) && periods.ValueKind == JsonValueKind.Array
                ? periods.EnumerateArray().Select(period => new { startTime = ProviderJson.Text(period, "start_time"), endTime = ProviderJson.Text(period, "end_time") }).ToArray()
                : Array.Empty<object>()
        }).ToArray();
    }

    private async Task<(JsonElement Menu, string Status)> ProviderMenu(CataloguePublication? latest, CancellationToken cancellationToken)
    {
        try
        {
            var current = await _menu.Read(cancellationToken);
            if (HasMenu(current)) return (current, "currentReadback");
        }
        catch (Exception exception) when (exception is ChannelConsoleException or HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        { }
        var baseline = latest?.State switch
        {
            CataloguePublicationStates.Verified => latest.Menu,
            CataloguePublicationStates.Pending or CataloguePublicationStates.Abandoned => latest.PreviousMenu,
            _ => default
        };
        return HasMenu(baseline) ? (baseline, "lastConfirmed") : (default, "unknown");
    }

    private static bool HasMenu(JsonElement value)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array;

    private static bool HasServiceAvailability(JsonElement value)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("menus", out var menus)
            && menus.ValueKind == JsonValueKind.Array && menus.GetArrayLength() > 0
            && menus[0].TryGetProperty("service_availability", out var days) && days.ValueKind == JsonValueKind.Array;

    private static JsonElement PlannedMenu(JsonElement preview, JsonElement template)
        => preview.ValueKind == JsonValueKind.Object && preview.TryGetProperty("menu", out var menu)
            && menu.ValueKind == JsonValueKind.Object ? menu : template;

    private static JsonElement FindProviderItem(JsonElement menu, string id)
    {
        if (menu.ValueKind != JsonValueKind.Object || !menu.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return default;
        foreach (var item in items.EnumerateArray())
            if (ProviderJson.Text(item, "id") == id) return item.Clone();
        return default;
    }

    private static JsonElement PublicationDto(CataloguePublication? publication, bool verified)
        => ProviderJson.Encode(new
        {
            id = publication?.Id ?? Guid.Empty,
            mappingRevision = publication?.MappingSnapshot is { } snapshot ? ProviderJson.Text(snapshot, "catalogueRevision") : string.Empty,
            publicationRevision = publication?.Revision ?? string.Empty,
            sourceRevision = publication?.SourceRevision ?? string.Empty,
            state = PublicationState(publication?.State),
            providerReadbackVerified = verified,
            providerMenuHash = publication?.ProviderHash,
            verifiedAt = publication?.VerifiedAt,
            resultCode = verified ? null : publication?.State == CataloguePublicationStates.Pending ? "ProviderReadbackPending" : "PublicationUnconfirmed"
        });

    private static object? PublicationSummary(CataloguePublication? publication)
        => publication is null ? null : new
        {
            id = publication.Id,
            mappingRevision = publication.MappingSnapshot is { } snapshot ? ProviderJson.Text(snapshot, "catalogueRevision") : string.Empty,
            publicationRevision = publication.Revision,
            state = PublicationState(publication.State),
            verifiedAt = publication.VerifiedAt,
            resultCode = PublicationState(publication.State) == "verified" ? null : "PublicationUnconfirmed"
        };

    private static string PublicationState(string? state) => state switch
    {
        CataloguePublicationStates.Verified => "verified",
        CataloguePublicationStates.Pending => "uncertain",
        CataloguePublicationStates.Abandoned => "abandoned",
        _ => "failed"
    };
}
