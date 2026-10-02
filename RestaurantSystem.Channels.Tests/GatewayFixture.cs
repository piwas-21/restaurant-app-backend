using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace RestaurantSystem.Channels.Tests;

public sealed class GatewayFixture : IAsyncLifetime
{
    public const string ClientId = "sandbox-client-fixture";
    // RFC 4231's public test key; never a provider credential.
    public const string SigningKey = "Jefe";
    public static readonly Guid StoreId = Guid.Parse("89dd9741-66b5-4bb4-b216-a813f3b21b4f");
    private PostgreSqlContainer? _postgres;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("CHANNEL_TESTS_DB_CONNECTION") ?? string.Empty;
        if (string.IsNullOrEmpty(ConnectionString))
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("channels_test").Build();
            await _postgres.StartAsync();
            ConnectionString = _postgres.GetConnectionString();
        }
        if (!string.Equals(new NpgsqlConnectionStringBuilder(ConnectionString).Database, "channels_test", StringComparison.Ordinal))
            throw new ArgumentException("Gateway tests require the disposable channels_test database.");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var migration = connection.CreateCommand();
        migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "001_webhook_inbox.sql"));
        await migration.ExecuteNonQueryAsync();
        migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "002_sandbox_console.sql"));
        await migration.ExecuteNonQueryAsync();
        migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "003_tenant_import_jobs.sql"));
        await migration.ExecuteNonQueryAsync();
        migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "004_order_observations.sql"));
        await migration.ExecuteNonQueryAsync();
        migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "005_created_order_recovery.sql"));
        await migration.ExecuteNonQueryAsync();
        migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "006_availability_sync.sql"));
        await migration.ExecuteNonQueryAsync();
        migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "007_catalogue_publications.sql"));
        await migration.ExecuteNonQueryAsync();
    }

    public WebApplicationFactory<Program> Host(string? signingKey = null, string? connectionString = null)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Channels"] = connectionString ?? ConnectionString,
                ["UberSandbox:ClientId"] = ClientId,
                ["UberSandbox:ClientSecret"] = signingKey ?? SigningKey,
                ["UberSandbox:StoreIds:0"] = StoreId.ToString(),
                ["UberSandbox:RequestsPerMinute"] = "1000",
            })));

    public async Task<long> Count(string eventId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT count(*) FROM channel_webhook_receipts WHERE event_id = $1";
        query.Parameters.AddWithValue(eventId);
        return (long)(await query.ExecuteScalarAsync() ?? 0L);
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("Channel gateway")]
public sealed class GatewayCollection : ICollectionFixture<GatewayFixture>;
