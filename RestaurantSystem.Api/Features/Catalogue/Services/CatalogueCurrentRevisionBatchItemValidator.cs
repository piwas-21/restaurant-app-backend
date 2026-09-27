using System.Text.Json;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueCurrentRevisionBatchItemValidator
{
    public static bool TryValidate(JsonElement item, HashSet<string> requestedIds, out string templateId)
    {
        templateId = string.Empty;
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("templateId", out var idValue) || idValue.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("status", out var statusValue) || statusValue.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("revision", out var revisionValue) ||
            !item.TryGetProperty("adoptedRevisionWithdrawn", out var adoptedWithdrawnValue) ||
            adoptedWithdrawnValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
        {
            return false;
        }

        templateId = idValue.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(templateId) || !requestedIds.Contains(templateId))
        {
            return false;
        }

        return statusValue.GetString() switch
        {
            "available" => CatalogueCurrentRevisionBatchRevisionValidator.IsValid(revisionValue, templateId),
            "withdrawn" => revisionValue.ValueKind == JsonValueKind.Null,
            "notFound" => revisionValue.ValueKind == JsonValueKind.Null &&
                adoptedWithdrawnValue.ValueKind == JsonValueKind.Null,
            _ => false
        };
    }
}
