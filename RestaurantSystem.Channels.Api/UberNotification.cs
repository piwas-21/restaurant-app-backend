using System.Security.Cryptography;
using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public static class UberNotification
{
    public static WebhookReceipt? Parse(byte[] body, string clientId, DateTimeOffset receivedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            var type = Text(root, "event_type");
            var meta = Object(root, "meta");
            var webhookMeta = Object(root, "webhook_meta");
            var declaredClient = Text(webhookMeta, "client_id");
            if (webhookMeta.ValueKind == JsonValueKind.Object && webhookMeta.TryGetProperty("client_id", out _)
                && !string.Equals(declaredClient, clientId, StringComparison.Ordinal))
                return null;

            // Order notifications and provisioning notifications use different envelopes.
            var eventId = Text(root, "event_id") ?? Text(webhookMeta, "webhook_msg_uuid");
            var store = Text(root, "store_id") ?? Text(meta, "user_id");
            var eventTime = Number(root, "event_time") ?? Number(webhookMeta, "webhook_msg_timestamp");
            var resource = Text(meta, "resource_id");
            if (type is null || eventId is null || !Guid.TryParse(store, out var storeId)
                || storeId == Guid.Empty || eventTime is null or <= 0)
                return null;

            // Notifications are references. resource_href is deliberately neither followed nor
            // persisted: a later adapter must build an allowlisted API URL from validated IDs.
            return new(clientId, eventId, type, storeId, resource, eventTime.Value,
                Convert.ToHexStringLower(SHA256.HashData(body)), receivedAt);
        }
        catch (JsonException) { return null; }
    }

    private static JsonElement Object(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? Text(JsonElement parent, string key)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(key, out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 128 ? text : null;
    }

    private static long? Number(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
}
