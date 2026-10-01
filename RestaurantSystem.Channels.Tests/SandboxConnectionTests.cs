using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class SandboxConnectionTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task AnonymousAndCrossOriginCallsAreDeniedAndLogoutInvalidatesSession()
    {
        using var anonymous = await Client.GetAsync("/api/sandbox/uber/receipts");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Client.DefaultRequestHeaders.Remove("Origin");
        using var crossOrigin = await Client.PostAsJsonAsync("/api/sandbox/auth/login", new { accessKey = AccessKey });
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        Client.DefaultRequestHeaders.Add("Origin", Origin);
        await Login();
        using var authorized = await Client.GetAsync("/api/sandbox/uber/receipts");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.Contains("no-store", authorized.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", Assert.Single(authorized.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        using var logout = await Client.PostAsJsonAsync("/api/sandbox/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var expired = await Client.GetAsync("/api/sandbox/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task AuthorizationUsesPkceOneTimeSessionBoundStateAndExactStore()
    {
        var session = await Session(); var other = await Session();
        var url = await InScope(s => s.GetRequiredService<ISandboxConnection>().Start(session, default));
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        Assert.Equal("sandbox-login.uber.com", new Uri(url).Host);
        Assert.Equal("eats.pos_provisioning", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(Origin + SandboxConsoleSettings.CallbackPath, query["redirect_uri"]);
        var state = query["state"].ToString();
        var wrongSession = await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(async s =>
        { await s.GetRequiredService<ISandboxConnection>().Complete(other, state, "public-fixture-code", "", default); return true; }));
        Assert.Equal(400, wrongSession.Status); Assert.Empty(Provider.Grants);
        await InScope(async s => { await s.GetRequiredService<ISandboxConnection>().Complete(session, state, "public-fixture-code", "", default); return true; });
        var merchantGrant = Assert.Single(Provider.Grants, g => g["grant_type"] == "authorization_code");
        Assert.Equal(query["code_challenge"], WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(merchantGrant["code_verifier"]))));
        var activation = Assert.Single(Provider.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/pos_data", StringComparison.Ordinal));
        Assert.True(activation.Body!.Value.GetProperty("is_order_manager").GetBoolean());
        Assert.True(activation.Body.Value.GetProperty("require_manual_acceptance").GetBoolean());
        Assert.Contains(GatewayFixture.StoreId.ToString(), activation.Path, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(async s =>
        { await s.GetRequiredService<ISandboxConnection>().Complete(session, state, "public-fixture-code", "", default); return true; }));
        Assert.Single(Provider.Grants, g => g["grant_type"] == "authorization_code");
        var stored = await InScope(s => s.GetRequiredService<IConsoleRepository>().FindToken(GatewayFixture.ClientId, GatewayFixture.StoreId, "app", default));
        Assert.NotNull(stored); Assert.DoesNotContain("app-token", stored.Cipher, StringComparison.Ordinal);
        Assert.Null(await InScope(s => s.GetRequiredService<IConsoleRepository>().FindToken(GatewayFixture.ClientId, GatewayFixture.StoreId, "merchant", default)));
    }

    [Fact]
    public async Task WrongMerchantNeverActivatesAndExpiredStateNeverExchangesCode()
    {
        Provider.MerchantStoreId = Guid.NewGuid();
        var session = await Session();
        var url = await InScope(s => s.GetRequiredService<ISandboxConnection>().Start(session, default));
        var state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        var failure = await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(async s =>
        { await s.GetRequiredService<ISandboxConnection>().Complete(session, state, "public-fixture-code", "", default); return true; }));
        Assert.Equal(403, failure.Status);
        Assert.DoesNotContain(Provider.Calls, c => c.Method == HttpMethod.Post);
        var expiredState = await InScope(async s =>
        {
            var crypto = s.GetRequiredService<ISandboxCrypto>(); var value = crypto.RandomToken();
            await s.GetRequiredService<IConsoleRepository>().SaveAuthorization(new(crypto.Hash(value), session, GatewayFixture.StoreId,
                crypto.Protect("public-fixture-verifier", "oauth:" + crypto.Hash(value)), DateTimeOffset.UtcNow.AddMinutes(-1)), default);
            return value;
        });
        await Assert.ThrowsAsync<ChannelConsoleException>(() => InScope(async s =>
        { await s.GetRequiredService<ISandboxConnection>().Complete(session, expiredState, "public-fixture-code", "", default); return true; }));
        Assert.Single(Provider.Grants);
    }

    [Fact]
    public async Task CallbackRedirectNeverContainsCredentialsAndConcurrentReplayExchangesOnce()
    {
        await Login();
        using var start = await Client.PostAsJsonAsync("/api/sandbox/uber/connect", new { });
        var url = (await Json(start)).GetProperty("url").GetString()!;
        var state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        var callbacks = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Client.GetAsync(
            SandboxConsoleSettings.CallbackPath + "?state=" + state + "&code=public-fixture-code")));
        foreach (var response in callbacks)
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.DoesNotContain("public-fixture-code", response.Headers.Location!.ToString(), StringComparison.Ordinal);
            response.Dispose();
        }
        Assert.Single(Provider.Grants, g => g["grant_type"] == "authorization_code");
    }

    [Fact]
    public async Task AppTokenIsCoalescedCachedAcrossScopesAndEncryptedWithBoundPurpose()
    {
        var tokens = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => InScope(s => s.GetRequiredService<ISandboxTokens>().AppToken(default))));
        Assert.All(tokens, token => Assert.Equal("app-token-not-for-logs", token)); Assert.Single(Provider.Grants);
        await InScope(s => s.GetRequiredService<ISandboxTokens>().AppToken(default)); Assert.Single(Provider.Grants);
        await InScope(s =>
        {
            var crypto = s.GetRequiredService<ISandboxCrypto>(); var cipher = crypto.Protect("public-test-plaintext", "store-a");
            Assert.Equal("public-test-plaintext", crypto.Unprotect(cipher, "store-a"));
            Assert.Throws<AuthenticationTagMismatchException>(() => crypto.Unprotect(cipher, "store-b"));
            return Task.FromResult(true);
        });
    }
}
