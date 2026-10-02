using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

internal static class UberOrderValue
{
    internal static JsonElement Object(JsonElement parent, string key)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : throw Unsupported();

    internal static string Text(JsonElement parent, string key, int max, bool optional = false, bool notes = false)
    {
        if (parent.ValueKind != JsonValueKind.Object) throw Unsupported();
        if ((!parent.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) && optional) return string.Empty;
        if (value.ValueKind != JsonValueKind.String) throw Unsupported();
        var text = value.GetString() ?? string.Empty;
        if (text.Length > max || !optional && string.IsNullOrWhiteSpace(text)
            || text.Any(c => char.IsControl(c) && !(notes && c is '\r' or '\n' or '\t'))) throw Unsupported();
        return text;
    }

    internal static decimal Money(JsonElement parent, string key, string currency)
    {
        var money = Object(parent, key);
        if (Text(money, "currency_code", 3) != currency
            || !money.TryGetProperty("amount", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var minor)
            || minor is < 0 or > 9_999_999_999) throw Unsupported();
        return minor / 100m;
    }

    internal static bool HasContent(JsonElement parent, string key)
    {
        if (parent.ValueKind != JsonValueKind.Object) throw Unsupported();
        return parent.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
            && !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);
    }

    internal static ChannelConsoleException Unsupported()
        => new(422, "The order cannot be represented by the approved catalogue and payment contract. Keep it held and use the marketplace fallback.");
}
