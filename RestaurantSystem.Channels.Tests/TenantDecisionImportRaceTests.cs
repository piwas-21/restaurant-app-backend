using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantDecisionImportRaceTests(GatewayFixture fixture)
{
    [Fact]
    public async Task VisibleTenantOrderBeforeGatewayImportConfirmationStaysRetryableThenDispatchesSameDecision()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var external = Guid.NewGuid(); var local = Guid.NewGuid();
        // The tenant committed, but its import response was lost: gateway still owns a Prepared job.
        await using var seed = source.CreateCommand("""
            INSERT INTO channel_import_jobs(client_id,store_id,order_id,tenant_id,catalogue_revision,state)
            VALUES ($1,$2,$3,'import-race-tenant','fixture-v1','Prepared')
            """);
        seed.Parameters.AddWithValue(GatewayFixture.ClientId); seed.Parameters.AddWithValue(store); seed.Parameters.AddWithValue(external);
        Assert.Equal(1, await seed.ExecuteNonQueryAsync());
        var lease = new TenantDecisionLease
        {
            DecisionId = Guid.NewGuid(),
            LeaseId = Guid.NewGuid(),
            OrderId = local,
            LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(2),
            Provider = "uber-eats",
            StoreId = store.ToString(),
            ExternalOrderId = external.ToString(),
            Action = "accept",
            Reason = "Prepare this visible test order.",
            Attempt = 1,
        };
        var destination = new Destination(lease); var provider = new Orders(lease);
        var settings = Options.Create(new TenantBridgeSettings
        { Enabled = true, DispatchDecisions = true, Store = new() { StoreId = store, TenantId = "import-race-tenant" } });
        TenantDecisionProcessor Processor() => new(settings, Options.Create(new UberWebhookSettings { ClientId = GatewayFixture.ClientId }),
            new PostgresChannelOrderLinks(source), new PostgresConsoleRepository(source), provider, destination, TimeProvider.System);
        Assert.True(await Processor().Process(default));
        Assert.Equal(0, provider.Calls); Assert.Equal("Unknown", Assert.Single(destination.Reports).State);
        Assert.Equal("UNKNOWN", destination.Reports[0].CanonicalState);
        // The durable original import retry finally confirms its retained local order UUID.
        await using var imported = source.CreateCommand("UPDATE channel_import_jobs SET state='Imported',tenant_order_id=$1 WHERE store_id=$2");
        imported.Parameters.AddWithValue(local); imported.Parameters.AddWithValue(store); Assert.Equal(1, await imported.ExecuteNonQueryAsync());
        destination.Lease = lease with { LeaseId = Guid.NewGuid(), Attempt = 2 };
        Assert.True(await Processor().Process(default));
        Assert.Equal(3, provider.Calls); Assert.Equal("Succeeded", destination.Reports[1].State);
        Assert.Equal("ACCEPTED", destination.Reports[1].CanonicalState);
    }

    private sealed class Orders(TenantDecisionLease lease) : ISandboxOrders
    {
        private bool _accepted;
        public int Calls { get; private set; }
        public Task<JsonElement> Read(string orderId, CancellationToken cancellationToken)
        {
            Calls++; return Task.FromResult(ProviderJson.Encode(new
            {
                id = lease.ExternalOrderId,
                store = new { id = lease.StoreId },
                current_state = _accepted ? "ACCEPTED" : "CREATED"
            }));
        }
        public Task<JsonElement> Decide(string orderId, string action, string reason, CancellationToken cancellationToken)
        {
            Assert.Equal(lease.ExternalOrderId, orderId); Assert.Equal(lease.Action, action); Assert.Equal(lease.Reason, reason);
            Calls++; _accepted = true; return Task.FromResult(ProviderJson.Encode(new { state = "Succeeded" }));
        }
        public Task<JsonElement> Receipts(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Destination(TenantDecisionLease lease) : ITenantDecisionClient
    {
        public TenantDecisionLease Lease { get; set; } = lease;
        public List<TenantDecisionReport> Reports { get; } = [];
        public Task<TenantDecisionLease?> Claim(TenantStoreBinding store, CancellationToken cancellationToken) => Task.FromResult<TenantDecisionLease?>(Lease);
        public Task Report(TenantStoreBinding store, TenantDecisionLease lease, TenantDecisionReport report, CancellationToken cancellationToken)
        { Reports.Add(report); return Task.CompletedTask; }
    }
}
