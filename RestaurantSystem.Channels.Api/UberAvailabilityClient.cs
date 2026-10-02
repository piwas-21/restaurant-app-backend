using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class UberAvailabilityClient(IUberSandboxClient provider, ISandboxTokens tokens, ISandboxMenu menu,
    TimeProvider clock) : IUberAvailabilityClient
{
    // Uber documents suspend_until as an int. Preserve indefinite Sofra stock intent up to its wire maximum;
    // refuse when this representation can no longer express a future suspension.
    public const int MaximumSuspensionTimestamp = int.MaxValue;

    public async Task<UberAvailabilitySnapshot> Read(TenantStoreBinding store, string clientId, CancellationToken cancellationToken)
    {
        var response = await provider.Send(HttpMethod.Get, $"/v2/eats/stores/{store.StoreId:D}/menus",
            await tokens.AppToken(cancellationToken), null, cancellationToken);
        ProviderJson.RequireSuccess(response, "availability readback");
        if (response.ClientId.Length > 0 && response.ClientId != clientId)
            throw InvalidMenu();
        SandboxMenuVerifier.Require(await menu.Expected(cancellationToken), response.Body);
        var expected = store.Items.Select(item => item.ProviderItemId).ToHashSet(StringComparer.Ordinal);
        var states = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var row in response.Body.GetProperty("items").EnumerateArray())
        {
            RequireSimpleItem(row);
            var id = ProviderJson.Text(row, "id");
            if (!expected.Contains(id) || !states.TryAdd(id, Available(row, clock.GetUtcNow().ToUnixTimeSeconds())))
                throw InvalidMenu();
        }
        if (states.Count != expected.Count) throw InvalidMenu();
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(response.Body.GetRawText()))), states);
    }

    internal static void RequireSimpleItem(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("price_info", out var price)
            || price.ValueKind != JsonValueKind.Object) throw InvalidMenu();
        if (price.TryGetProperty("overrides", out var overrides) && !EmptyCollection(overrides)) throw InvalidMenu();
        foreach (var key in new[] { "bundled_items", "modifier_group_ids", "quantity_info" })
        {
            if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0) continue;
            // Canonical defaults may contain empty ids/overrides, but no actual choice or quantity rules.
            if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(property =>
                property.Name is "ids" or "overrides" && EmptyCollection(property.Value))) continue;
            throw InvalidMenu();
        }
        // Full menu replacement must not erase unsupported provider stock rules either.
        _ = Available(row, 0);
    }

    private static bool EmptyCollection(JsonElement value)
        => value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0;

    public async Task Update(TenantStoreBinding store, TenantAvailabilityItem item, CancellationToken cancellationToken)
    {
        if (!store.Items.Any(mapping => mapping.ProviderItemId == item.ProviderItemId)
            || !item.Available && clock.GetUtcNow().ToUnixTimeSeconds() >= MaximumSuspensionTimestamp)
            throw new ChannelConsoleException(409, "Availability update is outside its reviewed mapping or suspension range.");
        var body = ProviderJson.Encode(new
        {
            suspension_info = new
            {
                suspension = item.Available ? null : new { suspend_until = MaximumSuspensionTimestamp, reason = "Unavailable in Sofra" },
                overrides = Array.Empty<object>(),
            },
        });
        var response = await provider.Send(HttpMethod.Post,
            $"/v2/eats/stores/{store.StoreId:D}/menus/items/{Uri.EscapeDataString(item.ProviderItemId)}",
            await tokens.AppToken(cancellationToken), body, cancellationToken);
        ProviderJson.RequireSuccess(response, "availability update");
    }

    private static bool Available(JsonElement row, long now)
    {
        if (!row.TryGetProperty("suspension_info", out var rules) || rules.ValueKind == JsonValueKind.Null) return true;
        if (rules.ValueKind != JsonValueKind.Object) throw InvalidMenu();
        if (rules.EnumerateObject().Any(property => property.Name is not ("suspension" or "overrides"))) throw InvalidMenu();
        if (rules.TryGetProperty("overrides", out var overrides) && overrides.ValueKind != JsonValueKind.Null
            && (overrides.ValueKind != JsonValueKind.Array || overrides.GetArrayLength() != 0)) throw InvalidMenu();
        if (!rules.TryGetProperty("suspension", out var suspension) || suspension.ValueKind == JsonValueKind.Null) return true;
        if (suspension.ValueKind != JsonValueKind.Object) throw InvalidMenu();
        if (suspension.EnumerateObject().Any(property => property.Name is not ("suspend_until" or "reason"))
            || suspension.TryGetProperty("reason", out var reason) && reason.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw InvalidMenu();
        if (!suspension.TryGetProperty("suspend_until", out var until) || until.ValueKind == JsonValueKind.Null) return true;
        return SuspensionExpired(until, now);
    }

    private static bool SuspensionExpired(JsonElement until, long now)
    {
        if (until.ValueKind != JsonValueKind.Number || !until.TryGetInt64(out var seconds)
            || seconds < 0 || seconds > MaximumSuspensionTimestamp) throw InvalidMenu();
        return seconds <= now;
    }

    private static ChannelConsoleException InvalidMenu()
        => new(502, "Uber availability readback is malformed or outside the reviewed catalogue.");
}
