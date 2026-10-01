using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public static class ProviderJson
{
    public static string Text(JsonElement json, string key)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
    public static bool Flag(JsonElement json, string key)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    public static bool? OptionalFlag(JsonElement json, string key)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    public static JsonElement Encode(object value) => JsonSerializer.SerializeToElement(value);
    public static void RequireSuccess(ProviderReply reply, string operation)
    {
        if (reply.IsSuccess) return;
        var code = Text(reply.Body, "code");
        if (code.Length == 0) code = Text(reply.Body, "error");
        if (code.Length > 80 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) code = "provider_error";
        throw new ChannelConsoleException(502, $"Uber {operation} returned HTTP {reply.Status} ({code}).");
    }
}
