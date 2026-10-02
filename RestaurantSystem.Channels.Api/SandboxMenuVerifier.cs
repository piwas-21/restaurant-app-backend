using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public static class SandboxMenuVerifier
{
    public static void Require(JsonElement expected, JsonElement actual)
    {
        foreach (var key in new[] { "menus", "categories", "items", "modifier_groups" })
        {
            if (!actual.TryGetProperty(key, out var rows))
                throw new ChannelConsoleException(409, "Publish the sandbox menu and verify its readback before enabling order testing.");
            // Uber serializes its empty modifier-group collection as null on GET.
            if (key == "modifier_groups" && rows.ValueKind == JsonValueKind.Null
                && expected.GetProperty(key).GetArrayLength() == 0) continue;
            if (rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() != expected.GetProperty(key).GetArrayLength())
                throw new ChannelConsoleException(409, "Publish the sandbox menu and verify its readback before enabling order testing.");
            foreach (var row in expected.GetProperty(key).EnumerateArray())
            {
                var found = rows.EnumerateArray().FirstOrDefault(r => ProviderJson.Text(r, "id") == ProviderJson.Text(row, "id"));
                if (found.ValueKind != JsonValueKind.Object || !Matches(row, found, key + "[]"))
                    throw new ChannelConsoleException(409, "Uber menu readback differs from the sandbox fixture. Refresh before retrying.");
            }
        }
    }

    // Provider-added defaults are allowed. Only observed category-item type omission is equivalent.
    private static bool Matches(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind != actual.ValueKind) return false;
        if (expected.ValueKind == JsonValueKind.Object)
            return expected.EnumerateObject().All(p =>
                actual.TryGetProperty(p.Name, out var value) ? Matches(p.Value, value, path + "." + p.Name)
                : path == "categories[].entities[]" && p.Name == "type" && p.Value.ValueKind == JsonValueKind.String
                    && p.Value.GetString() == "ITEM");
        if (expected.ValueKind == JsonValueKind.Array)
            return expected.GetArrayLength() == actual.GetArrayLength()
                && expected.EnumerateArray().Zip(actual.EnumerateArray()).All(p => Matches(p.First, p.Second, path + "[]"));
        return JsonElement.DeepEquals(expected, actual);
    }
}
