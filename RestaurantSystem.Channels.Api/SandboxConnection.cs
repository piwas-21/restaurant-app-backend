using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxConnection(IOptions<UberWebhookSettings> webhookOptions, ISandboxAuthorization authorization,
    ISandboxTokens tokens, IUberSandboxClient provider, ISandboxMenu menu) : ISandboxConnection
{
    private const int MaximumStoreDisplayNameLength = 160;

    private Guid StoreId => webhookOptions.Value.StoreIds.Single();
    private string StorePath => $"/v1/eats/stores/{StoreId:D}/pos_data";

    public Task<string> Start(string sessionHash, CancellationToken cancellationToken, bool enableTesting = false)
        => authorization.Start(sessionHash, enableTesting, cancellationToken);

    public async Task Complete(string sessionHash, string state, string code, string error, CancellationToken cancellationToken)
    {
        if (state.Length is < 32 or > 128 || code.Length > 8192 || error.Length > 128)
            throw new ChannelConsoleException(400, "Authorization response is invalid. Start a new connection.");
        var saved = await authorization.Claim(sessionHash, state, cancellationToken);
        if (error.Length > 0 || code.Length == 0)
            throw new ChannelConsoleException(400, "Uber authorization was not completed. Start a new connection.");
        var merchantToken = await tokens.Exchange(code, saved.Verifier, cancellationToken);
        await ConnectTenant(merchantToken, saved.EnableTesting, cancellationToken);
    }

    public async Task<JsonElement> ConnectTenant(string merchantToken, CancellationToken cancellationToken)
        => await ConnectTenant(merchantToken, false, cancellationToken);

    public async Task<JsonElement> ConnectTenant(string merchantToken, bool enableOrderAcceptance, CancellationToken cancellationToken)
    {
        await VerifyMerchant(merchantToken, cancellationToken);
        // Nominate using merchant consent, but retain tablet acceptance until menu verification.
        await Activate(merchantToken, true, cancellationToken);
        if (enableOrderAcceptance)
        {
            await menu.RequireVerified(cancellationToken);
            await Activate(merchantToken, false, cancellationToken);
        }
        return await Configuration(cancellationToken);
    }

    public async Task<JsonElement> Configuration(CancellationToken cancellationToken)
    {
        var result = await provider.Send(HttpMethod.Get, StorePath, await tokens.AppToken(cancellationToken), null, cancellationToken);
        ProviderJson.RequireSuccess(result, "integration configuration");
        var returnedStore = ProviderJson.Text(result.Body, "store_id");
        if (returnedStore.Length > 0 && !string.Equals(returnedStore, StoreId.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new ChannelConsoleException(502, "Uber returned a different store configuration.");
        return ProviderJson.Encode(new
        {
            storeId = StoreId,
            clientId = webhookOptions.Value.ClientId,
            enabled = ProviderJson.Flag(result.Body, "integration_enabled"),
            orderManager = string.Equals(ProviderJson.Text(result.Body, "order_manager_client_id"), webhookOptions.Value.ClientId, StringComparison.Ordinal)
                || result.ClientId.Length > 0 && string.Equals(ProviderJson.Text(result.Body, "order_manager_client_id"), result.ClientId, StringComparison.Ordinal),
            pending = ProviderJson.Flag(result.Body, "is_order_manager_pending"),
            manualAcceptance = ProviderJson.OptionalFlag(result.Body, "require_manual_acceptance"),
        });
    }

    public async Task<string?> StoreDisplayName(CancellationToken cancellationToken)
    {
        try
        {
            var path = $"/v1/eats/stores/{StoreId:D}";
            var result = await provider.Send(HttpMethod.Get, path, await tokens.AppToken(cancellationToken), null, cancellationToken);
            if (!result.IsSuccess || !Guid.TryParse(ProviderJson.Text(result.Body, "store_id"), out var returnedStore)
                || returnedStore != StoreId) return null;
            var name = ProviderJson.Text(result.Body, "name").Trim();
            return name.Length is > 0 and <= MaximumStoreDisplayNameLength && !name.Any(char.IsControl) ? name : null;
        }
        catch (ChannelConsoleException)
        {
            return null;
        }
    }

    public async Task<JsonElement> EnableOrders(bool enable, CancellationToken cancellationToken)
    {
        if (enable) throw new ChannelConsoleException(400, "Enable testing through a new merchant authorization.");
        var token = await tokens.AppToken(cancellationToken);
        var result = await provider.Send(HttpMethod.Patch, StorePath, token, ProviderJson.Encode(new
        {
            is_order_manager = false,
            integration_enabled = false,
            require_manual_acceptance = true,
        }), cancellationToken);
        ProviderJson.RequireSuccess(result, "order testing configuration");
        return await Configuration(cancellationToken);
    }

    private async Task Activate(string merchantToken, bool manualAcceptance, CancellationToken cancellationToken)
    {
        var activated = await provider.Send(HttpMethod.Post, StorePath, merchantToken, ProviderJson.Encode(new
        {
            is_order_manager = true,
            integrator_store_id = $"sofra-sandbox-{StoreId:D}",
            require_manual_acceptance = manualAcceptance,
            allowed_customer_requests = new { allow_special_instruction_requests = true, allow_single_use_items_requests = true },
        }), cancellationToken);
        ProviderJson.RequireSuccess(activated, "store activation");
    }

    private async Task VerifyMerchant(string token, CancellationToken cancellationToken)
    {
        string? next = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < 20; page++)
        {
            var path = QueryHelpers.AddQueryString("/v1/eats/stores", "limit", "50");
            if (!string.IsNullOrEmpty(next)) path = QueryHelpers.AddQueryString(path, "start_key", next);
            var result = await provider.Send(HttpMethod.Get, path, token, null, cancellationToken);
            ProviderJson.RequireSuccess(result, "merchant store discovery");
            if (!result.Body.TryGetProperty("stores", out var stores) || stores.ValueKind != JsonValueKind.Array)
                throw new ChannelConsoleException(502, "Uber returned an unexpected store list.");
            if (stores.EnumerateArray().Any(s => Guid.TryParse(ProviderJson.Text(s, "store_id"), out var id) && id == StoreId)) return;
            next = ProviderJson.Text(result.Body, "next_key");
            if (next.Length == 0 || next.Length > 4096 || !visited.Add(next)) break;
        }
        throw new ChannelConsoleException(403, "This Uber account cannot authorize the approved sandbox store. Use its test merchant account.");
    }
}
