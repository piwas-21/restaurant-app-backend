using System.Text.Json;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueCurrentRevisionBatchValidator
{
    public static bool IsValidResponse(
        JsonElement body,
        IReadOnlyList<CatalogueCurrentRevisionRequest> requestedItems)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != requestedItems.Count)
        {
            return false;
        }

        var requestedIds = requestedItems.Select(item => item.TemplateId).ToHashSet(StringComparer.Ordinal);
        var returnedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (!CatalogueCurrentRevisionBatchItemValidator.TryValidate(item, requestedIds, out var templateId) ||
                !returnedIds.Add(templateId))
            {
                return false;
            }
        }

        return returnedIds.SetEquals(requestedIds);
    }
}
