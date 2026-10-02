using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantMenuProvider(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    IUberSandboxClient provider, ISandboxTokens tokens) : ITenantMenuProvider
{
    private string MenuPath => $"/v2/eats/stores/{settings.Value.Store.StoreId:D}/menus";

    public async Task<JsonElement> Read(CancellationToken cancellationToken)
    {
        var reply = await provider.Send(HttpMethod.Get, MenuPath, await tokens.AppToken(cancellationToken), null, cancellationToken);
        RequireReply(reply, "tenant menu readback");
        if (reply.Body.ValueKind != JsonValueKind.Object || !reply.Body.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array) throw new ChannelConsoleException(502, "Uber returned a malformed tenant menu.");
        foreach (var item in items.EnumerateArray()) UberAvailabilityClient.RequireSimpleItem(item);
        return reply.Body;
    }

    public async Task Upload(JsonElement menu, CancellationToken cancellationToken)
        => RequireReply(await provider.Send(HttpMethod.Put, MenuPath, await tokens.AppToken(cancellationToken), menu,
            cancellationToken), "tenant menu upload");

    private void RequireReply(ProviderReply reply, string operation)
    {
        ProviderJson.RequireSuccess(reply, operation);
        if (reply.ClientId.Length > 0 && reply.ClientId != webhook.Value.ClientId)
            throw new ChannelConsoleException(502, "Uber returned tenant menu evidence for a different client.");
    }
}
