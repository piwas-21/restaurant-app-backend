using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantManagementOAuthTests(GatewayFixture fixture) : ConsoleFixture(fixture)
{
    [Fact]
    public async Task PkceCallbackIsBoundEncryptedSingleUseAndReturnsOnlyOpaqueFlowIdentity()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database); var actor = Guid.NewGuid(); var clock = new Clock(DateTimeOffset.UtcNow);
            var bridge = Bridge(); var oauth = OAuth(services, flows, bridge, clock);
            Provider.ManualAcceptance = true;
            var start = await oauth.Start(actor, false, default);
            var query = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl).Query);
            var state = query["state"].ToString();
            Assert.Equal(Origin + SandboxConsoleSettings.CallbackPath, query["redirect_uri"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            var binding = Binding(bridge); var stored = (await flows.Read(binding, start.FlowId, default))!;
            Assert.NotEqual(state, stored.StateHash);
            Assert.DoesNotContain("merchant", stored.VerifierCipher, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("pending", (await oauth.Read(start.FlowId, actor, default)).Status);
            await Assert.ThrowsAsync<ChannelConsoleException>(() => oauth.Read(start.FlowId, Guid.NewGuid(), default));
            Assert.Null(await flows.Read(binding with { TenantId = "other-tenant" }, start.FlowId, default));
            Assert.Null(await flows.Read(binding with { StoreId = Guid.NewGuid() }, start.FlowId, default));
            Assert.Null(await flows.Read(binding with { ClientId = "other-client" }, start.FlowId, default));
            Assert.Null(await oauth.CompleteIfKnown(new string('z', 64), "public-code-fixture", "", default));
            var callback = (await oauth.CompleteIfKnown(state, "public-code-fixture", "", default))!;
            Assert.Equal(start.FlowId, callback.FlowId);
            var returned = QueryHelpers.ParseQuery(new Uri(callback.ReturnUrl).Query);
            Assert.Equal(start.FlowId.ToString("D"), returned["flowId"]);
            Assert.Single(returned);
            Assert.DoesNotContain("code=", callback.ReturnUrl);
            Assert.Equal("connected", (await oauth.Read(start.FlowId, actor, default)).Status);
            Assert.Empty((await flows.Read(binding, start.FlowId, default))!.VerifierCipher);
            Assert.Single(Provider.Grants, grant => grant["grant_type"] == "authorization_code");
            await oauth.CompleteIfKnown(state, "replayed-code", "", default);
            Assert.Single(Provider.Grants, grant => grant["grant_type"] == "authorization_code");
            return true;
        });
    }

    [Fact]
    public async Task ExpiredStateAndForeignMerchantCannotActivateApprovedStore()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database); var actor = Guid.NewGuid(); var clock = new Clock(DateTimeOffset.UtcNow);
            var oauth = OAuth(services, flows, Bridge(), clock);
            var expired = await oauth.Start(actor, false, default);
            clock.Now = clock.Now.AddMinutes(11);
            await oauth.CompleteIfKnown(QueryHelpers.ParseQuery(new Uri(expired.AuthorizationUrl).Query)["state"].ToString(), "code", "", default);
            Assert.Equal("expired", (await oauth.Read(expired.FlowId, actor, default)).Status);
            Assert.Empty(Provider.Grants); Assert.Empty(Provider.Calls);
            var denied = await oauth.Start(actor, false, default);
            Provider.MerchantStoreId = Guid.NewGuid();
            await oauth.CompleteIfKnown(QueryHelpers.ParseQuery(new Uri(denied.AuthorizationUrl).Query)["state"].ToString(), "code", "", default);
            Assert.Equal("failed", (await oauth.Read(denied.FlowId, actor, default)).Status);
            Assert.DoesNotContain(Provider.Calls, call => call.Method == HttpMethod.Post);
            return true;
        });
    }

    [Fact]
    public async Task ExpiredClaimedFlowIsFailedWithoutRetainingPkceVerifier()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database); var actor = Guid.NewGuid(); var clock = new Clock(DateTimeOffset.UtcNow);
            var bridge = Bridge(); var oauth = OAuth(services, flows, bridge, clock);
            var started = await oauth.Start(actor, false, default);
            var state = QueryHelpers.ParseQuery(new Uri(started.AuthorizationUrl).Query)["state"].ToString();
            var stored = (await flows.FindByStateHash(Binding(bridge), services.GetRequiredService<ISandboxCrypto>().Hash(state), default))!;
            Assert.Equal("Processing", (await flows.Claim(Binding(bridge), stored.StateHash, clock.Now, default))!.Status);
            clock.Now = started.ExpiresAt.AddSeconds(1);
            var status = await oauth.Read(started.FlowId, actor, default);
            var expired = (await flows.Read(Binding(bridge), started.FlowId, default))!;
            Assert.Equal("failed", status.Status);
            Assert.Equal("ConnectionUnconfirmed", status.ErrorCode);
            Assert.Empty(expired.VerifierCipher);
            Assert.Empty(Provider.Grants);
            return true;
        });
    }

    private static TenantBridgeSettings Bridge() => new()
    { Enabled = true, Store = new() { StoreId = GatewayFixture.StoreId, TenantId = Guid.NewGuid().ToString(), CatalogueRevision = "oauth-fixture" } };
    private static AvailabilityBinding Binding(TenantBridgeSettings bridge)
        => new(GatewayFixture.ClientId, GatewayFixture.StoreId, bridge.Store.TenantId, bridge.Store.CatalogueRevision);
    private static TenantManagementOAuth OAuth(IServiceProvider services, ITenantOAuthFlows flows, TenantBridgeSettings bridge, TimeProvider clock)
        => new(Options.Create(bridge), Options.Create(new TenantManagementGatewaySettings
        {
            Enabled = true,
            ReturnUrl = "https://tenant.example/admin/delivery-channels/callback"
        }), services.GetRequiredService<IOptions<SandboxConsoleSettings>>(),
            services.GetRequiredService<IOptions<UberWebhookSettings>>(), flows, services.GetRequiredService<ISandboxCrypto>(),
            services.GetRequiredService<ISandboxTokens>(), services.GetRequiredService<ISandboxConnection>(), services.GetRequiredService<ISandboxMenu>(),
            services.GetRequiredService<IChannelManagementConnectionState>(), services.GetRequiredService<IChannelManagementAudit>(), clock);
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
