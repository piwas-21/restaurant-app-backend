using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantOAuthProvider(TenantManagementContext context,
    IOptions<SandboxConsoleSettings> consoleOptions, ISandboxCrypto crypto, ISandboxTokens tokens,
    ISandboxConnection connection, ISandboxMenu menu) : ITenantOAuthProvider
{
    private const string Scope = "eats.pos_provisioning";
    private const string AuthorizationPath = "/oauth/v2/authorize";
    private TenantManagementGatewaySettings Management => context.Management;
    private string CallbackUrl => consoleOptions.Value.PublicBaseUrl.TrimEnd('/') + SandboxConsoleSettings.CallbackPath;

    public async Task<TenantOAuthPreparation> Prepare(Guid flowId, Guid actorId, bool enableOrderAcceptance,
        CancellationToken cancellationToken)
    {
        if (enableOrderAcceptance) await menu.RequireVerified(cancellationToken);
        var store = context.ConfiguredStore;
        var now = context.Clock.GetUtcNow();
        var state = crypto.RandomToken();
        var verifier = crypto.RandomToken();
        var flow = new TenantOAuthFlow(flowId, context.Binding().ClientId, store.StoreId, store.TenantId, actorId,
            crypto.Hash(state), crypto.Protect(verifier, Purpose(flowId)), enableOrderAcceptance, "Pending", null,
            now, now.AddMinutes(Management.AuthorizationMinutes), null);
        return new(flow, AuthorizationUrl(state, verifier));
    }

    public string HashState(string state) => crypto.Hash(state);

    public async Task Connect(TenantOAuthFlow flow, string code, CancellationToken cancellationToken)
    {
        var verifier = crypto.Unprotect(flow.VerifierCipher, Purpose(flow.Id));
        var token = await tokens.Exchange(code, verifier, CallbackUrl, cancellationToken);
        var actual = await connection.ConnectTenant(token, flow.EnableOrderAcceptance, cancellationToken);
        var expectedManualAcceptance = !flow.EnableOrderAcceptance;
        if (ProviderJson.Text(actual, "storeId") != context.ConfiguredStore.StoreId.ToString("D")
            || !ProviderJson.Flag(actual, "enabled") || !ProviderJson.Flag(actual, "orderManager")
            || ProviderJson.Flag(actual, "pending")
            || ProviderJson.OptionalFlag(actual, "manualAcceptance") != expectedManualAcceptance)
            throw new ChannelConsoleException(409,
                "Uber has not confirmed this store connection. Refresh health before continuing.");
    }

    private string AuthorizationUrl(string state, string verifier)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = context.Webhook.ClientId,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["redirect_uri"] = CallbackUrl,
            ["state"] = state,
            ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256"
        };
        return QueryHelpers.AddQueryString(consoleOptions.Value.AuthBaseUrl.TrimEnd('/') + AuthorizationPath, query);
    }

    private static string Purpose(Guid flowId) => $"tenant-uber-oauth:{flowId:D}";
}
