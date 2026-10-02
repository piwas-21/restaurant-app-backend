using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class TenantObservationTests(GatewayFixture fixture)
{
    [Theory]
    [InlineData("ACCEPTED", false)]
    [InlineData("CANCELED", true)]
    [InlineData("DENIED", true)]
    [InlineData("FINISHED", true)]
    public async Task ExactDurableLinkGetsCanonicalStateAndTerminalStopsOnlyAfterTenantConfirmation(string state, bool terminal)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var job = await Seed(source); var repository = new PostgresChannelObservationJobs(source);
        var orders = new Orders(job, state); var destination = new Destination();
        var settings = Settings(job); TenantObservationProcessor Processor() => new(settings, Webhook(job), repository, orders, destination, TimeProvider.System);
        destination.LostResponse = true;
        Assert.True(await Processor().Process(default));
        Assert.Equal(1, orders.Reads); Assert.Single(destination.Received);
        await using var available = source.CreateCommand("UPDATE channel_order_observations SET available_at=now()-interval '1 second' WHERE store_id=$1");
        available.Parameters.AddWithValue(job.StoreId); Assert.Equal(1, await available.ExecuteNonQueryAsync());
        destination.LostResponse = false;
        Assert.True(await Processor().Process(default));
        Assert.Equal(2, orders.Reads); Assert.Equal(2, destination.Received.Count);
        var observation = destination.Received[1];
        Assert.Equal("uber-eats", observation.Provider); Assert.Equal(job.ExternalOrderId.ToString(), observation.ExternalOrderId);
        Assert.Equal(job.StoreId.ToString(), observation.StoreId); Assert.Equal(state, observation.CanonicalState);
        Assert.Matches("^[a-f0-9]{64}$", observation.CanonicalHash);
        await using var read = source.CreateCommand("SELECT terminal,canonical_state,canonical_hash FROM channel_order_observations WHERE store_id=$1");
        read.Parameters.AddWithValue(job.StoreId); await using var reader = await read.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(terminal, reader.GetBoolean(0)); Assert.Equal(state, reader.GetString(1));
        Assert.Equal(observation.CanonicalHash, reader.GetString(2)); await reader.CloseAsync();
        await available.ExecuteNonQueryAsync();
        Assert.Equal(!terminal, await Processor().Process(default));
        Assert.Equal(0, orders.Decisions);
    }

    [Fact]
    public async Task DisabledPausedForeignIdentitiesUnknownStateAndLeaseReplacementStaySafe()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString); var job = await Seed(source);
        var jobs = new PostgresChannelObservationJobs(source); var destination = new Destination(); var orders = new Orders(job, "ACCEPTED");
        var settings = Settings(job);
        TenantObservationProcessor Processor() => new(settings, Webhook(job), jobs, orders, destination, TimeProvider.System);
        settings.Value.Enabled = false; Assert.False(await Processor().Process(default));
        settings.Value.Enabled = true; settings.Value.Paused = true; Assert.False(await Processor().Process(default));
        Assert.Equal(0, orders.Reads);
        settings.Value.Paused = false; orders.State = "UNSUPPORTED"; Assert.True(await Processor().Process(default)); Assert.Empty(destination.Received);
        await using var available = source.CreateCommand("UPDATE channel_order_observations SET available_at=now()-interval '1 second' WHERE store_id=$1");
        available.Parameters.AddWithValue(job.StoreId); await available.ExecuteNonQueryAsync();
        orders.State = "CANCELED"; orders.WrongIdentity = true; Assert.True(await Processor().Process(default)); Assert.Empty(destination.Received);
        await available.ExecuteNonQueryAsync();
        Assert.Null(await jobs.Claim(job.ClientId + "other", job.StoreId, job.TenantId, default));
        Assert.Null(await jobs.Claim(job.ClientId, Guid.NewGuid(), job.TenantId, default));
        Assert.Null(await jobs.Claim(job.ClientId, job.StoreId, job.TenantId + "other", default));
        var leased = (await jobs.Claim(job.ClientId, job.StoreId, job.TenantId, default))!;
        Assert.Null(await jobs.Claim(job.ClientId, job.StoreId, job.TenantId, default));
        Assert.False(await jobs.Finish(leased with { TenantOrderId = Guid.NewGuid() }, "CANCELED", new string('a', 64), DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow, default));
        Assert.False(await jobs.Finish(leased with { LeaseId = Guid.NewGuid() }, "CANCELED", new string('a', 64), DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow, default));
        await using var expire = source.CreateCommand("UPDATE channel_order_observations SET lease_until=now()-interval '1 second' WHERE store_id=$1");
        expire.Parameters.AddWithValue(job.StoreId); await expire.ExecuteNonQueryAsync();
        var next = (await jobs.Claim(job.ClientId, job.StoreId, job.TenantId, default))!; Assert.NotEqual(leased.LeaseId, next.LeaseId);
        Assert.False(await jobs.Finish(leased, "CANCELED", new string('a', 64), DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow, default));
        Assert.True(await jobs.Finish(next, "CANCELED", new string('a', 64), DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task FailingOldestDueOrderDoesNotStarveAnotherImportedOrder()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var first = await Seed(source); var second = await Seed(source, first.StoreId);
        var jobs = new PostgresChannelObservationJobs(source);
        var claimed = (await jobs.Claim(first.ClientId, first.StoreId, first.TenantId, default))!;
        // A provider/tenant failure defers this exact job, permitting its sibling to progress.
        Assert.True(await jobs.Finish(claimed, null, null, null, false, DateTimeOffset.UtcNow.AddMinutes(1), default));
        var other = (await jobs.Claim(first.ClientId, first.StoreId, first.TenantId, default))!;
        Assert.NotEqual(claimed.ExternalOrderId, other.ExternalOrderId);
        Assert.Contains(other.ExternalOrderId, new[] { first.ExternalOrderId, second.ExternalOrderId });
        Assert.Null(await jobs.Claim(first.ClientId, first.StoreId, first.TenantId, default));
        Assert.True(await jobs.Finish(other, "CANCELED", new string('a', 64), DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow, default));
    }

    private static IOptions<TenantBridgeSettings> Settings(ChannelObservationJob job) => Options.Create(new TenantBridgeSettings
    { Enabled = true, Store = new() { StoreId = job.StoreId, TenantId = job.TenantId } });
    private static IOptions<UberWebhookSettings> Webhook(ChannelObservationJob job) => Options.Create(new UberWebhookSettings { ClientId = job.ClientId });
    private static async Task<ChannelObservationJob> Seed(NpgsqlDataSource source, Guid? storeId = null)
    {
        var job = new ChannelObservationJob(GatewayFixture.ClientId, storeId ?? Guid.NewGuid(), Guid.NewGuid(), "observation-tenant", Guid.NewGuid(), Guid.NewGuid());
        await using var seed = source.CreateCommand("""
            INSERT INTO channel_import_jobs(client_id,store_id,order_id,tenant_id,catalogue_revision,state,tenant_order_id)
            VALUES ($1,$2,$3,$4,'fixture-v1','Imported',$5)
            """);
        foreach (var value in new object[] { job.ClientId, job.StoreId, job.ExternalOrderId, job.TenantId, job.TenantOrderId }) seed.Parameters.Add(new() { Value = value });
        Assert.Equal(1, await seed.ExecuteNonQueryAsync()); return job;
    }
    private sealed class Orders(ChannelObservationJob job, string state) : ISandboxOrders
    {
        public int Reads { get; private set; }
        public int Decisions { get; private set; }
        public string State { get; set; } = state;
        public bool WrongIdentity { get; set; }
        public Task<JsonElement> Read(string orderId, CancellationToken token)
        {
            Assert.Equal(job.ExternalOrderId.ToString(), orderId); Reads++;
            return Task.FromResult(ProviderJson.Encode(new { id = WrongIdentity ? Guid.NewGuid().ToString() : orderId, store = new { id = job.StoreId }, current_state = State }));
        }
        public Task<JsonElement> Decide(string orderId, string action, string reason, CancellationToken token) { Decisions++; throw new NotSupportedException(); }
        public Task<JsonElement> Receipts(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Destination : ITenantObservationClient
    {
        public bool LostResponse { get; set; }
        public List<TenantOrderObservation> Received { get; } = [];
        public Task<bool> Observe(TenantStoreBinding store, Guid id, TenantOrderObservation observation, CancellationToken token)
        {
            Received.Add(observation); if (LostResponse) throw new ChannelConsoleException(504, "Response lost after tenant commit.");
            return Task.FromResult(observation.CanonicalState is "CANCELED" or "DENIED" or "FINISHED");
        }
    }
}
