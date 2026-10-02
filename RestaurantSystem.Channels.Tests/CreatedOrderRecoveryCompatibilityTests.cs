using Npgsql;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Tests;

[Collection("Channel gateway")]
public sealed class CreatedOrderRecoveryCompatibilityTests(GatewayFixture fixture)
{
    [Fact]
    public async Task LegacyBridgeWithoutRecoveryMigrationKeepsSignedReadsAndRefusesUnprovenOrders()
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        Assert.Equal("channels_test", builder.Database);
        await using var admin = NpgsqlDataSource.Create(builder.ConnectionString);
        // Dedicated disposable test database; never reuse or drop a database we did not create.
        await using (var create = admin.CreateCommand("CREATE DATABASE channels_recovery_compatibility"))
            await create.ExecuteNonQueryAsync();
        try
        {
            builder.Database = "channels_recovery_compatibility";
            await using var source = NpgsqlDataSource.Create(builder.ConnectionString);
            foreach (var name in new[] { "001_webhook_inbox.sql", "002_sandbox_console.sql", "003_tenant_import_jobs.sql" })
            {
                await using var migration = source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, name)));
                await migration.ExecuteNonQueryAsync();
            }
            var repository = new PostgresConsoleRepository(source); var id = Guid.NewGuid(); var store = Guid.NewGuid();
            Assert.False(await repository.HasOrder(GatewayFixture.ClientId, store, id.ToString(), default));
            Assert.Null(await repository.RecoveryEnrollment(GatewayFixture.ClientId, store, id, default));
            await new PostgresWebhookInbox(source).Receive(new(GatewayFixture.ClientId, Guid.NewGuid().ToString(), "orders.notification",
                store, id.ToString(), 1, new string('a', 64), DateTimeOffset.UtcNow), default);
            Assert.True(await repository.HasOrder(GatewayFixture.ClientId, store, id.ToString(), default));
            Assert.Null(await repository.RecoveryEnrollment(GatewayFixture.ClientId, store, id, default));
        }
        finally
        {
            await using var drop = admin.CreateCommand("DROP DATABASE channels_recovery_compatibility WITH (FORCE)");
            await drop.ExecuteNonQueryAsync();
        }
    }
}
