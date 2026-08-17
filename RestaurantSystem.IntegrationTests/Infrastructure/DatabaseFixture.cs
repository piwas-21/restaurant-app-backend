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
    private readonly Lock _sharedFactoryLock = new();
    private TestWebApplicationFactory? _sharedFactory;
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
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
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

    /// <summary>
    /// ONE test host, shared by every <see cref="IntegrationTestBase"/> class that neither
    /// overrides DI nor opts out via <c>RequiresIsolatedHost</c>.
    ///
    /// <para>
    /// Why: <see cref="IntegrationTestBase"/> used to build a brand-new
    /// <see cref="TestWebApplicationFactory"/> per TEST — ~800 host boots per run, which measured
    /// as ~91% of the DB-backed suite's wall clock (340s wall for 31.6s spent inside the test
    /// methods). Isolation does not come from the host: it comes from the Respawn wipe +
    /// re-seed that still runs before every single test. What the per-test host bought was
    /// isolation of in-memory singleton state, and only a handful of classes actually depend on
    /// that — those keep their own host (see <c>IntegrationTestBase.RequiresIsolatedHost</c>).
    /// </para>
    /// <para>
    /// Note the shared host's startup seeding (<c>MigrateApplicationDatabaseAsync</c>) now runs
    /// once instead of per test. Nothing regresses: that seed was already wiped by the Respawn
    /// reset that follows it in <c>InitializeAsync</c>, so no test could ever depend on it.
    /// </para>
    /// </summary>
    public TestWebApplicationFactory SharedFactory
    {
        get
        {
            lock (_sharedFactoryLock)
            {
                return _sharedFactory ??= new TestWebApplicationFactory(
                    ConnectionString, disableApplicationHostedServices: true);
            }
        }
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
        _sharedFactory?.Dispose();
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
