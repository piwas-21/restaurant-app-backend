using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxMenu(IOptions<UberWebhookSettings> options, IUberSandboxClient provider,
    ISandboxTokens tokens, IWebHostEnvironment environment, IOptions<TenantBridgeSettings>? bridge = null,
    ITenantCataloguePublication? catalogue = null) : ISandboxMenu
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
        if (bridge?.Value.UseTenantCatalogue == true)
            throw new ChannelConsoleException(409, "Review the tenant preview and submit its publication revision.");
        var result = await provider.Send(HttpMethod.Put, MenuPath, await tokens.AppToken(cancellationToken), Preview(), cancellationToken);
        ProviderJson.RequireSuccess(result, "sandbox menu upload");
        await RequireVerified(cancellationToken);
        return ProviderJson.Encode(new { verified = true, revision = "sandbox-menu-v1" });
    }

    public async Task RequireVerified(CancellationToken cancellationToken)
        => SandboxMenuVerifier.Require(await Expected(cancellationToken), await Read(cancellationToken));

    public Task<JsonElement> Preview(CancellationToken cancellationToken)
        => bridge?.Value.UseTenantCatalogue == true
            ? RequireCatalogue().Preview(Preview(), cancellationToken) : Task.FromResult(Preview());

    public Task<JsonElement> Expected(CancellationToken cancellationToken)
        => bridge?.Value.UseTenantCatalogue == true
            ? RequireCatalogue().Expected(cancellationToken) : Task.FromResult(Preview());

    public Task<JsonElement> Publish(string revision, CancellationToken cancellationToken)
        => bridge?.Value.UseTenantCatalogue == true
            ? RequireCatalogue().Publish(Preview(), revision, cancellationToken) : Publish(cancellationToken);

    private ITenantCataloguePublication RequireCatalogue()
        => catalogue ?? throw new ChannelConsoleException(409, "Tenant catalogue publication is unavailable.");
}
