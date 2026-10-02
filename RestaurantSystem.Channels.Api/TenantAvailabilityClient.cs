using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantAvailabilityClient(ITenantChannelTransport transport) : ITenantAvailabilityClient
{
    public async Task<TenantAvailabilitySnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken)
    {
        if (store.CatalogueApiToken.Length == 0 || store.Items.Count is < 1 or > 200)
            throw new ChannelConsoleException(409, "Availability requires its reviewed mapping and dedicated tenant read token.");
        var selections = store.Items.Select(item => (item.ProductId, item.VariationId)).Distinct().ToArray();
        // Per-call credentials, never a mutation of the shared order-ingress binding/token.
        var destination = new TenantStoreBinding { BaseUrl = store.BaseUrl, ApiToken = store.CatalogueApiToken };
        var response = await transport.Post(destination, "/api/delivery-channels/catalogue/availability", new
        {
            provider = "uber-eats",
            storeId = store.StoreId.ToString("D"),
            currency = store.Currency,
            isSandbox = true,
            items = selections.Select(item => new { item.ProductId, item.VariationId }),
        }, cancellationToken);
        var (revision, rows) = Envelope(response, store, selections.Length);
        var states = States(rows, selections.ToHashSet());
        return new(revision, store.Items.Select(item => new TenantAvailabilityItem(item.ProviderItemId,
            states[(item.ProductId, item.VariationId)].Available, states[(item.ProductId, item.VariationId)].Reason)).ToArray());
    }

    private static (string Revision, JsonElement Rows) Envelope(JsonElement? response, TenantStoreBinding store, int count)
    {
        if (response is not { ValueKind: JsonValueKind.Object } body || ProviderJson.Text(body, "provider") != "uber-eats"
            || ProviderJson.Text(body, "storeId") != store.StoreId.ToString("D") || ProviderJson.Text(body, "currency") != store.Currency
            || !body.TryGetProperty("isSandbox", out var sandbox) || sandbox.ValueKind != JsonValueKind.True
            || !body.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != count)
            throw InvalidSnapshot();
        var revision = ProviderJson.Text(body, "revision");
        if (revision.Length != 64 || revision.Any(character => !char.IsAsciiHexDigitLower(character))) throw InvalidSnapshot();
        return (revision, rows);
    }

    private static Dictionary<(Guid ProductId, Guid? VariationId), (bool Available, string Reason)> States(JsonElement rows,
        HashSet<(Guid ProductId, Guid? VariationId)> expected)
    {
        var states = new Dictionary<(Guid ProductId, Guid? VariationId), (bool Available, string Reason)>();
        foreach (var row in rows.EnumerateArray())
        {
            var key = Identity(row);
            if (!expected.Contains(key) || !row.TryGetProperty("available", out var available)
                || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidSnapshot();
            var reason = ProviderJson.Text(row, "reason");
            if (reason is not ("Available" or "MissingProduct" or "UnavailableProduct" or "DeliveryDisabled"
                or "UnsupportedChoices" or "UnavailableVariation" or "VariationRequired")
                || available.GetBoolean() != (reason == "Available") || !states.TryAdd(key, (available.GetBoolean(), reason)))
                throw InvalidSnapshot();
        }
        return states;
    }

    private static (Guid ProductId, Guid? VariationId) Identity(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || !Guid.TryParseExact(ProviderJson.Text(row, "productId"), "D", out var product)
            || product == Guid.Empty || !row.TryGetProperty("variationId", out var value)) throw InvalidSnapshot();
        Guid? variation = null;
        if (value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "D", out var id) || id == Guid.Empty)
                throw InvalidSnapshot();
            variation = id;
        }
        return (product, variation);
    }

    private static ChannelConsoleException InvalidSnapshot() => new(502, "The tenant availability snapshot is malformed or outside its reviewed mapping.");
}
