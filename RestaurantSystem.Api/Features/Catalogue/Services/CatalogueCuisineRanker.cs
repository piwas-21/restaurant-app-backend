using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueCuisineRanker
{
    public static JsonElement RankPage(JsonElement body, IReadOnlyList<string> preferredCuisines)
    {
        if (preferredCuisines.Count == 0 || body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return body;
        }

        var ranked = items.EnumerateArray()
            .Select((item, index) => new RankedItem(item, index, PreferenceRank(item, preferredCuisines)))
            .OrderBy(item => item.PreferencePriority)
            .ThenBy(item => item.OriginalOrder)
            .Select(item => JsonNode.Parse(item.Value.GetRawText()))
            .ToArray();
        var root = JsonNode.Parse(body.GetRawText())!.AsObject();
        root["items"] = new JsonArray(ranked);
        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }

    private static int PreferenceRank(JsonElement item, IReadOnlyList<string> preferredCuisines)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("cuisines", out var cuisines) ||
            cuisines.ValueKind != JsonValueKind.Array)
        {
            return preferredCuisines.Count;
        }

        var set = cuisines.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matching = Enumerable.Range(0, preferredCuisines.Count)
            .Where(index => set.Contains(preferredCuisines[index]))
            .DefaultIfEmpty(preferredCuisines.Count)
            .Min();
        return matching;
    }

    private sealed record RankedItem(JsonElement Value, int OriginalOrder, int PreferencePriority);
}
