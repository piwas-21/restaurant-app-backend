using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxMenu(IOptions<UberWebhookSettings> options, IUberSandboxClient provider,
    ISandboxTokens tokens, IWebHostEnvironment environment) : ISandboxMenu
{
    private string MenuPath => $"/v2/eats/stores/{options.Value.StoreIds.Single():D}/menus";

    public JsonElement Preview()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(environment.ContentRootPath, "Fixtures", "sandbox-menu-v1.json")));
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> Read(CancellationToken cancellationToken)
    {
        var result = await provider.Send(HttpMethod.Get, MenuPath, await tokens.AppToken(cancellationToken), null, cancellationToken);
        ProviderJson.RequireSuccess(result, "menu readback");
        return result.Body;
    }

    public async Task<JsonElement> Publish(CancellationToken cancellationToken)
    {
        var result = await provider.Send(HttpMethod.Put, MenuPath, await tokens.AppToken(cancellationToken), Preview(), cancellationToken);
        ProviderJson.RequireSuccess(result, "sandbox menu upload");
        await RequireVerified(cancellationToken);
        return ProviderJson.Encode(new { verified = true, revision = "sandbox-menu-v1" });
    }

    public async Task RequireVerified(CancellationToken cancellationToken)
    {
        var actual = await Read(cancellationToken);
        var expected = Preview();
        foreach (var key in new[] { "menus", "categories", "items", "modifier_groups" })
        {
            if (!actual.TryGetProperty(key, out var rows) || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() != expected.GetProperty(key).GetArrayLength())
                throw new ChannelConsoleException(409, "Publish the sandbox menu and verify its readback before enabling order testing.");
            foreach (var row in expected.GetProperty(key).EnumerateArray())
            {
                var found = rows.EnumerateArray().FirstOrDefault(r => ProviderJson.Text(r, "id") == ProviderJson.Text(row, "id"));
                if (found.ValueKind != JsonValueKind.Object || !Matches(row, found))
                    throw new ChannelConsoleException(409, "Uber menu readback differs from the sandbox fixture. Refresh before retrying.");
            }
        }
    }

    // Provider-added defaults are allowed; every field we published must survive readback.
    private static bool Matches(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind) return false;
        if (expected.ValueKind == JsonValueKind.Object)
            return expected.EnumerateObject().All(p => actual.TryGetProperty(p.Name, out var value) && Matches(p.Value, value));
        if (expected.ValueKind == JsonValueKind.Array)
            return expected.GetArrayLength() == actual.GetArrayLength()
                && expected.EnumerateArray().Zip(actual.EnumerateArray()).All(p => Matches(p.First, p.Second));
        return JsonElement.DeepEquals(expected, actual);
    }
}
