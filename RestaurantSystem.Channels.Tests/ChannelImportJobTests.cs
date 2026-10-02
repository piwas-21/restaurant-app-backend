using Npgsql;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class ChannelImportJobTests(GatewayFixture fixture)
{
    private async Task<(PostgresChannelImportJobs Jobs, NpgsqlDataSource Source, Guid Store, Guid Order)> Seed()
    {
        var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var order = Guid.NewGuid();
        await new PostgresWebhookInbox(source).Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(),
            "orders.notification", store, order.ToString(), 1, new string('a', 64), DateTimeOffset.UtcNow), default);
        var jobs = new PostgresChannelImportJobs(source);
        await jobs.Discover(GatewayFixture.ClientId, store, "test-tenant", "published-v1", DateTimeOffset.UtcNow.AddMinutes(-1).ToOffset(TimeSpan.FromHours(2)), default);
        return (jobs, source, store, order);
    }

    [Fact]
    public async Task DiscoveryIsIdempotentAndConcurrentClaimsLeaseOneIdentity()
    {
        var (jobs, source, store, order) = await Seed(); await using var owned = source;
        await jobs.Discover(GatewayFixture.ClientId, store, "test-tenant", "published-v2", DateTimeOffset.UtcNow.AddMinutes(-1), default);
        var claims = await Task.WhenAll(jobs.Claim(GatewayFixture.ClientId, store, "test-tenant", default),
            jobs.Claim(GatewayFixture.ClientId, store, "test-tenant", default));
        var claim = Assert.Single(claims, job => job is not null)!;
        Assert.Equal(order, claim.OrderId); Assert.Equal("published-v1", claim.CatalogueRevision);
        Assert.Null(await jobs.Claim(GatewayFixture.ClientId, store, "foreign-tenant", default));
        Assert.Null(await jobs.Claim("foreign-client", store, "test-tenant", default));
    }

    [Fact]
    public async Task PreparedRequestSurvivesRetryAndRestartAndCannotBeReplaced()
    {
        var (jobs, source, store, order) = await Seed(); await using var owned = source;
        var claim = (await jobs.Claim(GatewayFixture.ClientId, store, "test-tenant", default))!;
        var expires = DateTimeOffset.UtcNow.AddDays(1);
        Assert.True(await jobs.Prepare(claim, "ciphertext-only-fixture", new string('b', 64), expires, default));
        Assert.False(await jobs.Prepare(claim, "replacement", new string('c', 64), expires, default));
        Assert.True(await jobs.Defer(claim, "DeliveryUncertain", DateTimeOffset.UtcNow.AddSeconds(-1), false, default));
        var restarted = new PostgresChannelImportJobs(source);
        var retry = (await restarted.Claim(GatewayFixture.ClientId, store, "test-tenant", default))!;
        Assert.Equal(order, retry.OrderId); Assert.Equal("ciphertext-only-fixture", retry.EncryptedRequest);
        Assert.NotEqual(claim.LeaseId, retry.LeaseId);
        Assert.False(await jobs.Imported(claim, Guid.NewGuid(), default));
        var tenantId = Guid.NewGuid(); Assert.True(await restarted.Imported(retry, tenantId, default));
        Assert.Null(await restarted.Claim(GatewayFixture.ClientId, store, "test-tenant", default));
        await using var query = source.CreateCommand("SELECT state, encrypted_request, request_hash, tenant_order_id FROM channel_import_jobs WHERE store_id = $1");
        query.Parameters.AddWithValue(store);
        await using var row = await query.ExecuteReaderAsync(); Assert.True(await row.ReadAsync());
        Assert.Equal("Imported", row.GetString(0)); Assert.True(row.IsDBNull(1));
        Assert.Equal(new string('b', 64), row.GetString(2)); Assert.Equal(tenantId, row.GetGuid(3));
    }

    [Fact]
    public async Task ExpiredPreparedPayloadIsErasedWhileIdentityAndHashRemain()
    {
        var (jobs, source, store, _) = await Seed(); await using var owned = source;
        var claim = (await jobs.Claim(GatewayFixture.ClientId, store, "test-tenant", default))!;
        Assert.True(await jobs.Prepare(claim, "ciphertext-only-fixture", new string('b', 64), DateTimeOffset.UtcNow.AddSeconds(-1), default));
        await jobs.ExpirePayloads(default);
        Assert.Null(await jobs.Claim(GatewayFixture.ClientId, store, "test-tenant", default));
        await using var query = source.CreateCommand("SELECT state, encrypted_request, last_code, request_hash FROM channel_import_jobs WHERE store_id = $1");
        query.Parameters.AddWithValue(store);
        await using var row = await query.ExecuteReaderAsync(); Assert.True(await row.ReadAsync());
        Assert.Equal("Quarantined", row.GetString(0)); Assert.True(row.IsDBNull(1));
        Assert.Equal("PayloadExpired", row.GetString(2)); Assert.Equal(new string('b', 64), row.GetString(3));
    }

    [Fact]
    public async Task ForeignStoreMalformedResourceAndPreEnrollmentNotificationsCannotCreateJobs()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var inbox = new PostgresWebhookInbox(source);
        await inbox.Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(), "orders.notification", store,
            "not-an-order-id", 1, new string('a', 64), DateTimeOffset.UtcNow), default);
        await inbox.Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(), "orders.notification", Guid.NewGuid(),
            Guid.NewGuid().ToString(), 1, new string('a', 64), DateTimeOffset.UtcNow), default);
        await inbox.Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(), "orders.notification", store,
            Guid.NewGuid().ToString(), 1, new string('a', 64), DateTimeOffset.UtcNow.AddHours(-1)), default);
        var jobs = new PostgresChannelImportJobs(source);
        await jobs.Discover(GatewayFixture.ClientId, store, "test-tenant", "published-v1", DateTimeOffset.UtcNow.AddMinutes(-1), default);
        Assert.Null(await jobs.Claim(GatewayFixture.ClientId, store, "test-tenant", default));
    }
}
