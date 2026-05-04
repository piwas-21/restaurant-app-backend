using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using RestaurantSystem.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// When set, the fixture connects to this Postgres instead of starting a
    /// Testcontainers container. Used in CI where dind is unreliable on the
    /// docker+machine executor — the pipeline declares a postgres service
    /// and exports its URL into this variable. Locally, leave unset and
    /// Testcontainers spins up its own.
    /// </summary>
    private const string ExternalConnectionEnv = "INTEGRATION_TESTS_DB_CONNECTION";

    private PostgreSqlContainer? _postgres;
    private Respawner _respawner = null!;
    // Shared DataSource (one connection pool for the whole test run). Without
    // this, CreateContext() built a fresh DataSource per call and each got its
    // own pool — once enough tests ran, Postgres hit max_connections (53300).
    private NpgsqlDataSource _dataSource = null!;

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalConnectionEnv);
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
        }
        else
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("restaurant_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();

            await _postgres.StartAsync();
            ConnectionString = _postgres.GetConnectionString();
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();

        // Run migrations
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        // Setup Respawner for database cleanup between tests
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public" },
            TablesToIgnore = new Table[]
            {
                "__EFMigrationsHistory",
                // Singleton seeded by the AddRestaurantInfo migration —
                // ignore so per-test reset doesn't wipe it.
                "RestaurantInfo",
                "RestaurantPhoneNumbers",
            }
        });
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        return new ApplicationDbContext(options);
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
