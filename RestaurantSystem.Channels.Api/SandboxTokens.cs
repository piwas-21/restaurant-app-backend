using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxTokens(IOptions<UberWebhookSettings> webhookOptions, IOptions<SandboxConsoleSettings> consoleOptions,
    IUberSandboxClient provider, IConsoleRepository repository, ISandboxCrypto crypto, TimeProvider timeProvider,
    TokenRefreshLock refreshLock) : ISandboxTokens
{
    private const string AppScopes = "eats.store eats.order";
    private const string CreatedOrderScope = "eats.store.orders.read";

    public Task<string> AppToken(CancellationToken cancellationToken) => CachedToken("app", AppScopes, cancellationToken);

    public Task<string> CreatedOrdersToken(CancellationToken cancellationToken)
        => CachedToken("created-orders", CreatedOrderScope, cancellationToken);

    private async Task<string> CachedToken(string kind, string scopes, CancellationToken cancellationToken)
    {
        await refreshLock.Gate.WaitAsync(cancellationToken);
        try { return await CachedAppToken(kind, scopes, cancellationToken); }
        finally { refreshLock.Gate.Release(); }
    }

    private async Task<string> CachedAppToken(string kind, string scopes, CancellationToken cancellationToken)
    {
        var settings = webhookOptions.Value;
        var store = settings.StoreIds.Single();
        var saved = await repository.FindToken(settings.ClientId, store, kind, cancellationToken);
        if (saved is not null && saved.ExpiresAt > timeProvider.GetUtcNow().AddMinutes(5))
            return crypto.Unprotect(saved.Cipher, Purpose(kind));
        var token = await provider.Token(Fields("client_credentials", new() { ["scope"] = scopes }), cancellationToken);
        ProviderJson.RequireSuccess(token, "authentication");
        RequireToken(token.Body, scopes);
        var access = ProviderJson.Text(token.Body, "access_token");
        await repository.SaveToken(new(settings.ClientId, store, kind, crypto.Protect(access, Purpose(kind)),
            timeProvider.GetUtcNow().AddSeconds(token.Body.GetProperty("expires_in").GetInt32())), cancellationToken);
        return access;
    }

    public Task<string> Exchange(string code, string verifier, CancellationToken cancellationToken)
        => Exchange(code, verifier, consoleOptions.Value.PublicBaseUrl.TrimEnd('/') + SandboxConsoleSettings.CallbackPath,
            cancellationToken);

    public async Task<string> Exchange(string code, string verifier, string redirectUri, CancellationToken cancellationToken)
    {
        var expectedRedirect = consoleOptions.Value.PublicBaseUrl.TrimEnd('/') + SandboxConsoleSettings.CallbackPath;
        if (!string.Equals(redirectUri, expectedRedirect, StringComparison.Ordinal))
            throw new ChannelConsoleException(400, "Authorization callback does not match the registered Uber redirect.");
        var token = await provider.Token(Fields("authorization_code", new()
        {
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
        }), cancellationToken);
        ProviderJson.RequireSuccess(token, "merchant authorization");
        RequireToken(token.Body, "eats.pos_provisioning");
        // Merchant token exists only during store discovery/activation, never in a cookie or database.
        return ProviderJson.Text(token.Body, "access_token");
    }

    private Dictionary<string, string> Fields(string grant, Dictionary<string, string> extra)
    {
        extra["client_id"] = webhookOptions.Value.ClientId;
        extra["client_secret"] = webhookOptions.Value.ClientSecret;
        extra["grant_type"] = grant;
        return extra;
    }

    private string Purpose(string kind) => $"uber-sandbox:{webhookOptions.Value.ClientId}:{webhookOptions.Value.StoreIds.Single():D}:{kind}";

    private static void RequireToken(JsonElement token, string scopes)
    {
        if (ProviderJson.Text(token, "access_token").Length == 0 || !string.Equals(ProviderJson.Text(token, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase)
            || !token.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt32(out var seconds) || seconds is <= 300 or > 2_592_000
            || scopes.Split(' ').Except(ProviderJson.Text(token, "scope").Split(' '), StringComparer.Ordinal).Any())
            throw new ChannelConsoleException(502, "Uber did not return the expected token permissions or expiry.");
    }
}
