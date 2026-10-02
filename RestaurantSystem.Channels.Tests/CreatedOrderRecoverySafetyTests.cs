using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class CreatedOrderRecoverySafetyTests(GatewayFixture fixture)
{
    [Fact]
    public async Task RecoveryFailureNeverLogsProviderPayloadOrCredentials()
    {
        await using var services = new ServiceCollection().AddScoped<ICreatedOrderRecovery>(_ => new FaultingRecovery()).BuildServiceProvider();
        var logger = new CaptureLogger();
        using var worker = new CreatedOrderRecoveryWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TenantBridgeSettings { RecoveryPollSeconds = 5 }), TimeProvider.System, logger);
        await worker.StartAsync(default);
        var message = await logger.Emitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(default);
        Assert.DoesNotContain(FaultingRecovery.PrivateFixture, message, StringComparison.Ordinal);
        Assert.Equal("Sandbox created-order recovery failed; review its permissions and store backlog.", message);
        Assert.Null(logger.Exception);
    }

    [Theory]
    [InlineData("invalid-hash")]
    [InlineData("before-enrollment")]
    public async Task InvalidEvidenceRollsBackTheEntireBatch(string mode)
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var id = Guid.NewGuid(); var enrolled = DateTimeOffset.UtcNow.AddMinutes(-1);
        var hash = mode == "invalid-hash" ? new string('g', 64) : new string('a', 64);
        var badTime = mode == "before-enrollment" ? enrolled.AddSeconds(-1) : enrolled.AddSeconds(1);
        await Assert.ThrowsAsync<PostgresException>(() => new PostgresCreatedOrderDiscoveries(source).Record(
            GatewayFixture.ClientId, store, "safety-tenant", "recovery-v1", enrolled,
            [new(id, enrolled.AddSeconds(1), new string('a', 64)), new(Guid.NewGuid(), badTime, hash)], default));
        await using var query = source.CreateCommand("SELECT count(*) FROM channel_import_jobs WHERE store_id = $1");
        query.Parameters.AddWithValue(store); Assert.Equal(0L, await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DatabaseRejectsNullProviderEvidenceRatherThanTreatingUnknownCheckAsTrue()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        await using var query = source.CreateCommand("""
            INSERT INTO channel_import_jobs (client_id, store_id, order_id, tenant_id, catalogue_revision,
              discovery_source, discovery_hash, created_at, recovery_enrolled_at)
            VALUES ($1, $2, $3, 'safety-tenant', 'recovery-v1', 'provider_poll', NULL, now(), now() - interval '1 minute')
            """);
        query.Parameters.AddWithValue(GatewayFixture.ClientId); query.Parameters.AddWithValue(Guid.NewGuid());
        query.Parameters.AddWithValue(Guid.NewGuid());
        var error = await Assert.ThrowsAsync<PostgresException>(() => query.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task WebhookFirstPreparedRequestCannotBeReplacedByPollingOrDifferentBinding()
    {
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = Guid.NewGuid(); var order = Guid.NewGuid(); var enrolled = DateTimeOffset.UtcNow.AddMinutes(-1);
        await new PostgresWebhookInbox(source).Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(), "orders.notification",
            store, order.ToString(), 1, new string('a', 64), DateTimeOffset.UtcNow), default);
        var jobs = new PostgresChannelImportJobs(source);
        await jobs.Discover(GatewayFixture.ClientId, store, "original-tenant", "original-revision", enrolled, default);
        var claim = (await jobs.Claim(GatewayFixture.ClientId, store, "original-tenant", default))!;
        Assert.True(await jobs.Prepare(claim, "public-ciphertext-fixture", new string('b', 64), DateTimeOffset.UtcNow.AddDays(1), default));
        await new PostgresCreatedOrderDiscoveries(source).Record(GatewayFixture.ClientId, store, "foreign-tenant", "foreign-revision",
            enrolled, [new(order, enrolled.AddSeconds(1), new string('c', 64))], default);
        await using var query = source.CreateCommand("SELECT tenant_id, catalogue_revision, discovery_source, encrypted_request FROM channel_import_jobs WHERE order_id = $1");
        query.Parameters.AddWithValue(order); await using var row = await query.ExecuteReaderAsync(); Assert.True(await row.ReadAsync());
        Assert.Equal("original-tenant", row.GetString(0)); Assert.Equal("original-revision", row.GetString(1));
        Assert.Equal("webhook", row.GetString(2)); Assert.Equal("public-ciphertext-fixture", row.GetString(3));
    }

    private sealed class FaultingRecovery : ICreatedOrderRecovery
    {
        public const string PrivateFixture = "fixture-phone-and-token-that-must-not-reach-logs";
        public Task Process(CancellationToken cancellationToken) => Task.FromException(new ChannelConsoleException(502, PrivateFixture));
    }

    private sealed class CaptureLogger : ILogger<CreatedOrderRecoveryWorker>
    {
        public TaskCompletionSource<string> Emitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Exception = exception; Emitted.TrySetResult(formatter(state, exception)); }
    }
}
