using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantDecisionProcessorTests(GatewayFixture fixture)
{
    private static readonly Guid ExternalId = Guid.Parse("6107841e-d2c0-4051-88b6-edb5236e32c2");
    private static TenantDecisionLease Lease(Guid store) => new()
    {
        DecisionId = Guid.NewGuid(),
        LeaseId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2),
        Provider = "uber-eats",
        StoreId = store.ToString(),
        ExternalOrderId = ExternalId.ToString(),
        Action = "accept",
        Reason = "Prepare this test meal.",
        Attempt = 1,
    };
    private static TenantBridgeSettings Settings(Guid store) => new()
    {
        Enabled = true,
        DispatchDecisions = true,
        Store = new() { StoreId = store, TenantId = "decision-fixture-tenant" },
    };
    private static TenantDecisionProcessor Processor(TenantBridgeSettings settings, IChannelOrderLinks links,
        IConsoleRepository repo, ISandboxOrders orders, ITenantDecisionClient tenant)
        => new(Options.Create(settings), Options.Create(new UberWebhookSettings { ClientId = GatewayFixture.ClientId }),
            links, repo, orders, tenant, TimeProvider.System);

    [Theory]
    [InlineData("ACCEPTED", "accept", "Succeeded")]
    [InlineData("DENIED", "deny", "Succeeded")]
    [InlineData("FINISHED", "accept", "Succeeded")]
    [InlineData("CANCELED", "accept", "Failed")]
    [InlineData("CREATED", "accept", "Unknown")]
    public async Task PostAcknowledgmentRequiresIndependentCanonicalEvidence(string afterPost, string action, string result)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var lease = Lease(store) with { Action = action };
        var destination = new Destination(lease); var orders = new Orders(store, "CREATED", afterPost);
        Assert.True(await Processor(Settings(store), new Links(), new PostgresConsoleRepository(source), orders, destination).Process(default));
        Assert.Equal(1, orders.Decisions); Assert.Equal(2, orders.Reads);
        var report = Assert.Single(destination.Reports); Assert.Equal(result, report.State);
        Assert.Equal(afterPost, report.CanonicalState); Assert.Matches("^[a-f0-9]{64}$", report.CanonicalHash);
        Assert.Equal(lease.LeaseId, report.LeaseId);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Unknown")]
    [InlineData("Succeeded")]
    public async Task DurableProviderActionNeverResendsEvenWhileCanonicalStateRemainsCreated(string recorded)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var lease = Lease(store); var repo = new PostgresConsoleRepository(source);
        Assert.True(await repo.ClaimAction(GatewayFixture.ClientId, store, ExternalId.ToString(), "accept", default));
        await repo.FinishAction(GatewayFixture.ClientId, store, ExternalId.ToString(), recorded, default);
        var destination = new Destination(lease); var orders = new Orders(store, "CREATED");
        for (var restart = 0; restart < 2; restart++)
            Assert.True(await Processor(Settings(store), new Links(), new PostgresConsoleRepository(source), orders, destination).Process(default));
        Assert.Equal(0, orders.Decisions); Assert.Equal(2, orders.Reads);
        Assert.All(destination.Reports, report => Assert.Equal("Unknown", report.State));
    }

    [Fact]
    public async Task LostTenantReportRecoversAcceptedProviderStateWithoutAnotherPost()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var lease = Lease(store); var repo = new PostgresConsoleRepository(source);
        var destination = new Destination(lease) { LoseFirstReport = true }; var orders = new Orders(store, "CREATED", "ACCEPTED");
        await Assert.ThrowsAsync<ChannelConsoleException>(() => Processor(Settings(store), new Links(), repo, orders, destination).Process(default));
        destination.Lease = lease with { LeaseId = Guid.NewGuid(), Attempt = 2 };
        Assert.True(await Processor(Settings(store), new Links(), repo, orders, destination).Process(default));
        Assert.Equal(1, orders.Decisions); Assert.Equal(3, orders.Reads);
        Assert.All(destination.Reports, report => Assert.Equal("Succeeded", report.State));
        Assert.Equal(destination.Lease.LeaseId, destination.Reports[1].LeaseId);
    }

    [Fact]
    public async Task WrongImportedIdentityAndPausedDisabledDispatchNeverTouchProvider()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var settings = Settings(store); var lease = Lease(store);
        var destination = new Destination(lease); var orders = new Orders(store, "CREATED", "ACCEPTED");
        Assert.True(await Processor(settings, new Links(false), new PostgresConsoleRepository(source), orders, destination).Process(default));
        Assert.Equal("Unknown", Assert.Single(destination.Reports).State);
        Assert.Equal("UNKNOWN", destination.Reports[0].CanonicalState); Assert.Equal(0, orders.Reads); Assert.Equal(0, orders.Decisions);
        settings.Paused = true;
        Assert.False(await Processor(settings, new Links(), new PostgresConsoleRepository(source), orders, destination).Process(default));
        settings.Paused = false; settings.DispatchDecisions = false;
        Assert.False(await Processor(settings, new Links(), new PostgresConsoleRepository(source), orders, destination).Process(default));
        settings.DispatchDecisions = true; settings.Enabled = false;
        Assert.False(await Processor(settings, new Links(), new PostgresConsoleRepository(source), orders, destination).Process(default));
        Assert.Equal(1, destination.Claims);
    }

    [Theory]
    [InlineData("DENIED")]
    [InlineData("UNKNOWN_PROVIDER_STATE")]
    public async Task ConflictingOrUnrecognizedCanonicalEvidenceStaysOnHold(string canonicalState)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var destination = new Destination(Lease(store)); var orders = new Orders(store, canonicalState);
        Assert.True(await Processor(Settings(store), new Links(), new PostgresConsoleRepository(source), orders, destination).Process(default));
        Assert.Equal(0, orders.Decisions);
        Assert.Equal("Unknown", Assert.Single(destination.Reports).State); Assert.Empty(destination.Reports[0].CanonicalHash);
    }

    private sealed class Links(bool matches = true) : IChannelOrderLinks
    {
        public Task<bool> IsImported(string clientId, Guid storeId, string tenantId, Guid externalOrderId, Guid tenantOrderId, CancellationToken cancellationToken)
            => Task.FromResult(matches);
    }
    private sealed class Orders(Guid store, string initial, string? after = null) : ISandboxOrders
    {
        public int Reads { get; private set; }
        public int Decisions { get; private set; }
        public Task<JsonElement> Read(string orderId, CancellationToken cancellationToken)
        {
            Reads++; return Task.FromResult(ProviderJson.Encode(new { id = ExternalId, store = new { id = store }, current_state = Decisions > 0 ? after ?? initial : initial }));
        }
        public Task<JsonElement> Decide(string orderId, string action, string reason, CancellationToken cancellationToken)
        {
            Assert.Equal(ExternalId.ToString(), orderId); Assert.Equal("Prepare this test meal.", reason);
            Decisions++; return Task.FromResult(ProviderJson.Encode(new { state = "Succeeded" }));
        }
        public Task<JsonElement> Receipts(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Destination(TenantDecisionLease lease) : ITenantDecisionClient
    {
        public TenantDecisionLease Lease { get; set; } = lease;
        public int Claims { get; private set; }
        public bool LoseFirstReport { get; init; }
        public List<TenantDecisionReport> Reports { get; } = [];
        public Task<TenantDecisionLease?> Claim(TenantStoreBinding store, CancellationToken cancellationToken)
        { Claims++; return Task.FromResult<TenantDecisionLease?>(Lease); }
        public Task Report(TenantStoreBinding store, TenantDecisionLease lease, TenantDecisionReport report, CancellationToken cancellationToken)
        {
            Reports.Add(report);
            if (LoseFirstReport && Reports.Count == 1) throw new ChannelConsoleException(502, "Public fixture lost tenant result.");
            return Task.CompletedTask;
        }
    }
}
