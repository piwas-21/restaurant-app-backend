using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantManagementOAuth(IOptions<TenantBridgeSettings> bridgeOptions,
    IOptions<TenantManagementGatewaySettings> managementOptions, IOptions<SandboxConsoleSettings> consoleOptions,
    IOptions<UberWebhookSettings> webhookOptions, ITenantOAuthFlows flows, ISandboxCrypto crypto,
    ISandboxTokens tokens, ISandboxConnection connection, ISandboxMenu menu,
    IChannelManagementConnectionState connectionState, IChannelManagementAudit audit, TimeProvider clock) : ITenantManagementOAuth
{
    private TenantStoreBinding Store => bridgeOptions.Value.Store;
    private AvailabilityBinding Binding => new(webhookOptions.Value.ClientId, Store.StoreId, Store.TenantId, Store.CatalogueRevision);
    private string CallbackUrl => consoleOptions.Value.PublicBaseUrl.TrimEnd('/') + SandboxConsoleSettings.CallbackPath;

    public async Task<TenantOAuthStart> Start(Guid actorId, bool enableOrderAcceptance, CancellationToken cancellationToken)
    {
        if (!bridgeOptions.Value.Enabled || !managementOptions.Value.Enabled || actorId == Guid.Empty) throw Disabled();
        if (enableOrderAcceptance) await menu.RequireVerified(cancellationToken);
        var flowId = Guid.NewGuid(); var state = crypto.RandomToken(); var verifier = crypto.RandomToken();
        var now = clock.GetUtcNow(); var expires = now.AddMinutes(managementOptions.Value.AuthorizationMinutes);
        await flows.Create(new(flowId, Binding.ClientId, Store.StoreId, Store.TenantId, actorId, crypto.Hash(state),
            crypto.Protect(verifier, Purpose(flowId)), enableOrderAcceptance, "Pending", null, now, expires, null), cancellationToken);
        await audit.Record(Binding, actorId, "OAuthStart", "Pending", flowId, now, cancellationToken);
        var url = QueryHelpers.AddQueryString(consoleOptions.Value.AuthBaseUrl.TrimEnd('/') + "/oauth/v2/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = webhookOptions.Value.ClientId,
                ["response_type"] = "code",
                ["scope"] = "eats.pos_provisioning",
                ["redirect_uri"] = CallbackUrl,
                ["state"] = state,
                ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["code_challenge_method"] = "S256",
            });
        return new(flowId, url, expires);
    }

    public async Task<TenantOAuthStatus> Read(Guid flowId, Guid actorId, CancellationToken cancellationToken)
    {
        await flows.Expire(Binding, clock.GetUtcNow(), cancellationToken);
        var flow = await flows.Read(Binding, flowId, cancellationToken);
        if (flow is null || flow.ActorId != actorId || actorId == Guid.Empty) throw new ChannelConsoleException(404, "Authorization flow was not found.");
        return Status(flow);
    }

    public async Task<TenantOAuthCallback?> CompleteIfKnown(string state, string code, string error,
        CancellationToken cancellationToken)
    {
        if (!managementOptions.Value.Enabled || !bridgeOptions.Value.Enabled || state.Length is < 32 or > 128) return null;
        var now = clock.GetUtcNow();
        await flows.Expire(Binding, now, cancellationToken);
        var known = await flows.FindByStateHash(Binding, crypto.Hash(state), cancellationToken);
        if (known is null) return null;
        var flow = await flows.Claim(Binding, known.StateHash, now, cancellationToken);
        if (flow is null || flow.Status == "Expired") return Callback(known.Id);
        if (flow.Status != "Processing") return Callback(flow.Id);
        if (code.Length is 0 or > 8192 || error.Length > 128 || error.Length > 0)
        {
            await Finish(flow, "Failed", "AuthorizationDenied", cancellationToken);
            return Callback(flow.Id);
        }
        try
        {
            var verifier = crypto.Unprotect(flow.VerifierCipher, Purpose(flow.Id));
            var merchantToken = await tokens.Exchange(code, verifier, CallbackUrl, cancellationToken);
            var actual = await connection.ConnectTenant(merchantToken, flow.EnableOrderAcceptance, cancellationToken);
            var expectedManual = !flow.EnableOrderAcceptance;
            if (ProviderJson.Text(actual, "storeId") != Store.StoreId.ToString("D")
                || !ProviderJson.Flag(actual, "enabled") || !ProviderJson.Flag(actual, "orderManager")
                || ProviderJson.Flag(actual, "pending")
                || ProviderJson.OptionalFlag(actual, "manualAcceptance") != expectedManual)
                throw new ChannelConsoleException(409, "Uber has not confirmed this store connection. Refresh health before continuing.");
            if (await Finish(flow, "Connected", null, cancellationToken))
                await connectionState.Set(Binding, false, flow.ActorId, clock.GetUtcNow(), cancellationToken);
        }
        catch (ChannelConsoleException)
        {
            await Finish(flow, "Failed", "ConnectionUnconfirmed", cancellationToken);
        }
        catch (HttpRequestException)
        {
            await Finish(flow, "Failed", "ConnectionUnconfirmed", cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await Finish(flow, "Failed", "ConnectionUnconfirmed", cancellationToken);
        }
        return Callback(flow.Id);
    }

    private async Task<bool> Finish(TenantOAuthFlow flow, string status, string? code, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (!await flows.Finish(Binding, flow.Id, status, code, now, cancellationToken)) return false;
        await audit.Record(Binding, flow.ActorId, "OAuth", status, flow.Id, now, cancellationToken);
        return true;
    }

    private TenantOAuthCallback Callback(Guid id)
        => new(id, QueryHelpers.AddQueryString(managementOptions.Value.ReturnUrl, "flowId", id.ToString("D")));

    private TenantOAuthStatus Status(TenantOAuthFlow flow)
        => new(flow.Id, flow.Status switch
        {
            "Pending" or "Processing" => "pending",
            "Connected" => "connected",
            "Expired" => "expired",
            _ => "failed"
        }, Store.StoreId, flow.Status == "Connected",
            flow.CreatedAt, flow.ExpiresAt, flow.CompletedAt, flow.ErrorCode);

    private static string Purpose(Guid flowId) => $"tenant-uber-oauth:{flowId:D}";
    private static ChannelConsoleException Disabled() => new(404, "Tenant delivery authorization is not enabled.");
}
