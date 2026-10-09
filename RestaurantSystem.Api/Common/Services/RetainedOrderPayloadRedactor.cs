using System.Text.Json;
using System.Text.Json.Nodes;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>Removes operational free text while preserving the typed financial/item structure.</summary>
internal static class RetainedOrderPayloadRedactor
{
    internal static string? Redact(string? json)
    {
        if (json is null)
            return null;
        try
        {
            var payload = JsonNode.Parse(json)
                ?? throw new JsonException("The retained order payload is null.");
            if (payload is not (JsonArray or JsonObject))
                throw new JsonException("The retained order payload has no structured fields.");
            Visit(payload);
            return payload.ToJsonString();
        }
        catch (JsonException exception)
        {
            throw new ConflictException(
                "Retained order instructions could not be erased safely; reconcile the stored payload first.",
                exception);
        }
    }

    private static void Visit(JsonNode node)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array.OfType<JsonNode>())
                Visit(child);
            return;
        }
        if (node is not JsonObject item)
            return;

        foreach (var property in item.ToArray())
        {
            if (IsOperationalFreeText(property.Key))
                item[property.Key] = null;
            else if (property.Value is not null)
                Visit(property.Value);
        }
    }

    private static bool IsOperationalFreeText(string name) =>
        name.Equals("specialInstructions", StringComparison.OrdinalIgnoreCase)
        || name.Equals("reason", StringComparison.OrdinalIgnoreCase)
        || name.Equals("providerConsentNote", StringComparison.OrdinalIgnoreCase);
}
