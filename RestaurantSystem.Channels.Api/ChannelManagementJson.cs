using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantSystem.Channels.Api;

internal static class ChannelManagementJson
{
    internal static bool? NullableFlag(JsonElement value, string name)
        => value.TryGetProperty(name, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? field.GetBoolean() : null;

    internal static DateTimeOffset? NullableTime(JsonElement value, string name)
        => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            && field.TryGetDateTimeOffset(out var date) ? date : null;

    internal static JsonElement Result(Guid id, string status, string? code, bool requestSent,
        DateTimeOffset? observedAt = null)
        => ProviderJson.Encode(new { operationId = id, status, code, observedAt, providerRequestSent = requestSent });

    internal static Guid StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
