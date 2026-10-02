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
        => SandboxMenuVerifier.Require(Preview(), await Read(cancellationToken));
}
