using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;
using System.Text.Json;

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

    [Fact]
    public async Task CallbackHoldsTheStoreLeaseForTheProviderConnectionTransition()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database); var bridge = Bridge();
            var clock = new Clock(DateTimeOffset.UtcNow);
            var oauth = OAuth(services, flows, bridge, clock);
            var actor = Guid.NewGuid();
            var jobs = services.GetRequiredService<IChannelAvailabilityJobs>();
            var audit = services.GetRequiredService<IChannelManagementAudit>();
            var connectionState = services.GetRequiredService<IChannelManagementConnectionState>();
            var operations = Operations(services, bridge, flows, jobs, audit, connectionState, TimeProvider.System);
            var started = await oauth.Start(actor, false, default);
            var state = QueryHelpers.ParseQuery(new Uri(started.AuthorizationUrl).Query)["state"].ToString();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Provider.ManualAcceptance = true;
            Provider.BeforeConnectionConfiguration = async () =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            };

            var callback = oauth.CompleteIfKnown(state, "public-code-fixture", "", default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var providerCalls = Provider.Calls.Count;
            await Assert.ThrowsAsync<ChannelConsoleException>(() => operations.Disconnect(bridge.Store.StoreId, actor, default));
            await Assert.ThrowsAsync<ChannelConsoleException>(() => oauth.Start(Guid.NewGuid(), false, default));
            clock.Now = started.ExpiresAt.AddSeconds(1);
            Assert.Equal("pending", (await oauth.Read(started.FlowId, actor, default)).Status);
            await oauth.CompleteIfKnown(state, "duplicate-code", "", default);
            Assert.Equal(providerCalls, Provider.Calls.Count);
            Assert.False((await connectionState.Read(Binding(bridge), default)).IsDisconnected);
            Assert.Null(await services.GetRequiredService<IChannelAvailabilityOverrides>().Read(Binding(bridge), default));
            Assert.Equal("Processing", (await flows.Read(Binding(bridge), started.FlowId, default))!.Status);
            release.TrySetResult();
            await callback;
            var flow = (await flows.Read(Binding(bridge), started.FlowId, default))!;
            Assert.Equal("Connected", flow.Status);
            return true;
        });
    }

    [Fact]
    public async Task DisconnectCancellationMakesAStaleAuthorizationCallbackReadOnly()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database); var bridge = Bridge();
            var oauth = OAuth(services, flows, bridge, new Clock(DateTimeOffset.UtcNow));
            var started = await oauth.Start(Guid.NewGuid(), false, default);
            var state = QueryHelpers.ParseQuery(new Uri(started.AuthorizationUrl).Query)["state"].ToString();
            var otherBinding = Binding(bridge) with { StoreId = Guid.NewGuid() };
            var otherId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await flows.Create(new(otherId, otherBinding.ClientId, otherBinding.StoreId, otherBinding.TenantId,
                Guid.NewGuid(), services.GetRequiredService<ISandboxCrypto>().Hash(Guid.NewGuid().ToString()),
                "encrypted-verifier-fixture", false, "Pending", null, now, now.AddMinutes(5), null), default);
            Assert.Equal(1, await flows.CancelPending(Binding(bridge), "ConnectionDisconnected", DateTimeOffset.UtcNow, default));

            var response = await oauth.CompleteIfKnown(state, "stale-authorization-code", "", default);

            Assert.Equal(started.FlowId, response!.FlowId);
            var flow = (await flows.Read(Binding(bridge), started.FlowId, default))!;
            Assert.Equal("Failed", flow.Status);
            Assert.Equal("ConnectionDisconnected", flow.ErrorCode);
            Assert.Empty(flow.VerifierCipher);
            Assert.Equal("Pending", (await flows.Read(otherBinding, otherId, default))!.Status);
            Assert.Empty(Provider.Grants);
            Assert.Empty(Provider.Calls);
            return true;
        });
    }

    [Fact]
    public async Task BusyCallbackFailsOnlyItsPendingFlowAndFreshAuthorizationCanRetry()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database); var bridge = Bridge(); var binding = Binding(bridge);
            var actor = Guid.NewGuid(); var oauth = OAuth(services, flows, bridge, new Clock(DateTimeOffset.UtcNow));
            var started = await oauth.Start(actor, false, default);
            var state = QueryHelpers.ParseQuery(new Uri(started.AuthorizationUrl).Query)["state"].ToString();
            var crypto = services.GetRequiredService<ISandboxCrypto>(); var unrelatedId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await flows.Create(new(unrelatedId, binding.ClientId, binding.StoreId, binding.TenantId, actor,
                crypto.Hash(Guid.NewGuid().ToString()), "other-encrypted-verifier", false, "Pending", null,
                now, now.AddMinutes(5), null), default);
            var jobs = services.GetRequiredService<IChannelAvailabilityJobs>();
            await using (var lease = await jobs.TryLease(binding, default))
            {
                Assert.NotNull(lease);
                var callback = await oauth.CompleteIfKnown(state, "single-use-code", "", default);
                Assert.Equal(started.FlowId, callback!.FlowId);
                var failed = (await flows.Read(binding, started.FlowId, default))!;
                Assert.Equal("Failed", failed.Status);
                Assert.Equal("ConnectionOperationBusy", failed.ErrorCode);
                Assert.Empty(failed.VerifierCipher);
                var untouched = (await flows.Read(binding, unrelatedId, default))!;
                Assert.Equal("Pending", untouched.Status);
                Assert.Equal("other-encrypted-verifier", untouched.VerifierCipher);
                Assert.Empty(Provider.Grants);
                Assert.Empty(Provider.Calls);
            }

            Provider.ManualAcceptance = true;
            var retry = await oauth.Start(actor, false, default);
            var retryState = QueryHelpers.ParseQuery(new Uri(retry.AuthorizationUrl).Query)["state"].ToString();
            await oauth.CompleteIfKnown(retryState, "fresh-code", "", default);
            Assert.Equal("Connected", (await flows.Read(binding, retry.FlowId, default))!.Status);
            Assert.Single(Provider.Grants, grant => grant["grant_type"] == "authorization_code");
            return true;
        });
    }

    [Fact]
    public async Task OAuthStartAuditFailureCreatesNoFlowOrProviderRequest()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database); var bridge = Bridge();
            var oauth = OAuth(services, flows, bridge, new Clock(DateTimeOffset.UtcNow), new FailingAudit());

            await Assert.ThrowsAsync<InvalidOperationException>(() => oauth.Start(Guid.NewGuid(), false, default));

            await using var count = database.CreateCommand("SELECT count(*) FROM channel_tenant_oauth_flows WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3");
            count.Parameters.AddWithValue(Binding(bridge).ClientId); count.Parameters.AddWithValue(Binding(bridge).StoreId);
            count.Parameters.AddWithValue(Binding(bridge).TenantId);
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
            Assert.Empty(Provider.Grants);
            Assert.Empty(Provider.Calls);
            return true;
        });
    }

    [Fact]
    public async Task LatestAuthorizationIntentSupersedesPendingFlowsAcrossAcceptanceModes()
    {
        await InScope(async services =>
        {
            await using var database = NpgsqlDataSource.Create(Database.ConnectionString);
            var flows = new PostgresTenantOAuthFlows(database);
            var bridge = Bridge(); var binding = Binding(bridge); var actor = Guid.NewGuid();
            var oauth = OAuth(services, flows, bridge, new Clock(DateTimeOffset.UtcNow),
                menu: new VerifiedMenu(), connection: new ConfirmedConnection());

            Provider.ManualAcceptance = true;
            var initial = await oauth.Start(actor, false, default);
            var initialState = State(initial.AuthorizationUrl);
            var enableAcceptance = await oauth.Start(actor, true, default);
            var initialFlow = (await flows.Read(binding, initial.FlowId, default))!;
            Assert.Equal("Failed", initialFlow.Status);
            Assert.Equal("AuthorizationSuperseded", initialFlow.ErrorCode);
            Assert.Empty(initialFlow.VerifierCipher);
            await oauth.CompleteIfKnown(initialState, "superseded-initial-code", "", default);
            Assert.Empty(Provider.Grants);
            Assert.Empty(Provider.Calls);

            Provider.ManualAcceptance = false;
            await oauth.CompleteIfKnown(State(enableAcceptance.AuthorizationUrl), "enable-acceptance-code", "", default);
            var enabledFlow = (await flows.Read(binding, enableAcceptance.FlowId, default))!;
            Assert.True(enabledFlow.Status == "Connected", $"Unexpected outcome: {enabledFlow.Status}/{enabledFlow.ErrorCode}");
            Assert.Single(Provider.Grants);

            var olderEnabled = await oauth.Start(actor, true, default);
            var manualOnly = await oauth.Start(actor, false, default);
            Assert.Equal("AuthorizationSuperseded", (await flows.Read(binding, olderEnabled.FlowId, default))!.ErrorCode);
            await oauth.CompleteIfKnown(State(olderEnabled.AuthorizationUrl), "stale-enabled-code", "", default);
            Assert.Single(Provider.Grants);
            Provider.ManualAcceptance = true;
            await oauth.CompleteIfKnown(State(manualOnly.AuthorizationUrl), "manual-acceptance-code", "", default);
            Assert.Equal("Connected", (await flows.Read(binding, manualOnly.FlowId, default))!.Status);
            Assert.Equal(2, Provider.Grants.Count);
            return true;
        });
    }

    private static TenantBridgeSettings Bridge() => new()
    {
        Enabled = true,
        SyncAvailability = true,
        UseTenantCatalogue = true,
        Store = new()
        {
            StoreId = GatewayFixture.StoreId,
            TenantId = Guid.NewGuid().ToString(),
            CatalogueApiToken = "catalogue-token-fixture",
            CatalogueRevision = "oauth-fixture",
            Items = [new() { ProviderItemId = "meal", ProductId = Guid.NewGuid() }]
        }
    };
    private static AvailabilityBinding Binding(TenantBridgeSettings bridge)
        => new(GatewayFixture.ClientId, GatewayFixture.StoreId, bridge.Store.TenantId, bridge.Store.CatalogueRevision);
    private static TenantManagementOAuth OAuth(IServiceProvider services, ITenantOAuthFlows flows, TenantBridgeSettings bridge,
        TimeProvider clock, IChannelManagementAudit? audit = null, ISandboxMenu? menu = null,
        ISandboxConnection? connection = null)
    {
        var management = Options.Create(new TenantManagementGatewaySettings
        {
            Enabled = true,
            ReturnUrl = "https://tenant.example/admin/delivery-channels/callback"
        });
        var context = new TenantManagementContext(Options.Create(bridge), management,
            services.GetRequiredService<IOptions<UberWebhookSettings>>(), clock);
        var provider = new TenantOAuthProvider(context, services.GetRequiredService<IOptions<SandboxConsoleSettings>>(),
            services.GetRequiredService<ISandboxCrypto>(), services.GetRequiredService<ISandboxTokens>(),
            connection ?? services.GetRequiredService<ISandboxConnection>(), menu ?? services.GetRequiredService<ISandboxMenu>());
        var coordinator = new TenantOAuthStateCoordinator(context, flows, provider,
            services.GetRequiredService<IChannelAvailabilityJobs>(), services.GetRequiredService<IChannelManagementConnectionState>(),
            audit ?? services.GetRequiredService<IChannelManagementAudit>());
        return new TenantManagementOAuth(context, coordinator);
    }

    private static string State(string authorizationUrl)
        => QueryHelpers.ParseQuery(new Uri(authorizationUrl).Query)["state"].ToString();

    private sealed class VerifiedMenu : ISandboxMenu
    {
        public JsonElement Preview() => ProviderJson.Encode(new { items = Array.Empty<object>() });
        public Task<JsonElement> Preview(CancellationToken cancellationToken) => Task.FromResult(Preview());
        public Task<JsonElement> Read(CancellationToken cancellationToken) => Task.FromResult(Preview());
        public Task<JsonElement> Publish(CancellationToken cancellationToken) => Task.FromResult(Preview());
        public Task<JsonElement> Publish(string revision, CancellationToken cancellationToken) => Task.FromResult(Preview());
        public Task RequireVerified(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<JsonElement> Expected(CancellationToken cancellationToken) => Task.FromResult(Preview());
    }

    private sealed class ConfirmedConnection : ISandboxConnection
    {
        public Task<string> Start(string sessionHash, CancellationToken cancellationToken, bool enableTesting = false)
            => Task.FromResult(string.Empty);
        public Task Complete(string sessionHash, string state, string code, string error, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<JsonElement> ConnectTenant(string merchantToken, CancellationToken cancellationToken)
            => ConnectTenant(merchantToken, false, cancellationToken);
        public Task<JsonElement> ConnectTenant(string merchantToken, bool enableOrderAcceptance, CancellationToken cancellationToken)
            => Task.FromResult(ProviderJson.Encode(new
            {
                storeId = GatewayFixture.StoreId,
                enabled = true,
                orderManager = true,
                pending = false,
                manualAcceptance = !enableOrderAcceptance
            }));
        public Task<JsonElement> Configuration(CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
        public Task<JsonElement> EnableOrders(bool enable, CancellationToken cancellationToken) => Task.FromResult(ProviderJson.Encode(new { }));
    }
    private static TenantChannelManagementOperations Operations(IServiceProvider services, TenantBridgeSettings bridge,
        ITenantOAuthFlows flows, IChannelAvailabilityJobs jobs, IChannelManagementAudit audit,
        IChannelManagementConnectionState connectionState, TimeProvider clock)
    {
        var webhook = services.GetRequiredService<IOptions<UberWebhookSettings>>();
        var management = Options.Create(new TenantManagementGatewaySettings { Enabled = true });
        var context = new TenantManagementContext(Options.Create(bridge), management, webhook, clock);
        var catalogueState = new TenantCatalogueManagementState(services.GetRequiredService<ICatalogueMappingDrafts>(),
            services.GetRequiredService<ICataloguePublications>(), services.GetRequiredService<ICatalogueMappingResolver>(), jobs);
        var availabilityState = new TenantChannelAvailabilityState(services.GetRequiredService<ICatalogueMappingResolver>(), jobs,
            services.GetRequiredService<IChannelAvailabilityOverrides>());
        var connection = services.GetRequiredService<ISandboxConnection>();
        var menu = services.GetRequiredService<ISandboxMenu>();
        var status = services.GetRequiredService<IChannelAvailabilityStatus>();
        var tenantAvailability = services.GetRequiredService<ITenantAvailabilityClient>();
        var summary = new TenantChannelSummaryService(context, connectionState, services.GetRequiredService<ICataloguePublications>(), connection, status);
        var catalogue = new TenantChannelCatalogueService(context, catalogueState, menu, services.GetRequiredService<ITenantCatalogueClient>(),
            services.GetRequiredService<ITenantCataloguePublication>(), audit);
        var availability = new TenantChannelAvailabilityService(context, availabilityState, status, tenantAvailability, audit);
        var exceptionData = new TenantChannelExceptionData(context, catalogueState, availabilityState,
            services.GetRequiredService<IChannelImportView>(), audit, flows, connectionState);
        var exceptions = new TenantChannelExceptionService(context, exceptionData,
            new TenantChannelPublicationReconciler(context, catalogueState, menu, audit),
            new TenantChannelAvailabilityReconciler(context, availabilityState, tenantAvailability,
                services.GetRequiredService<IUberAvailabilityClient>(), audit));
        var connectionService = new TenantChannelConnectionService(context, connection, flows, connectionState, availabilityState, audit);
        return new(summary, catalogue, availability, exceptions, connectionService);
    }
    private sealed class FailingAudit : IChannelManagementAudit
    {
        public Task Record(AvailabilityBinding binding, Guid actorId, string action, string resultCode,
            Guid? operationId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("Audit store unavailable."));
        public Task<IReadOnlyList<ChannelManagementAuditRecord>> Read(AvailabilityBinding binding, long? beforeSequence,
            int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChannelManagementAuditRecord>>([]);
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
