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

    /// <summary>
    /// The next transaction id the cluster will assign. Only a WRITING transaction consumes one,
    /// so an unchanged value proves the database has not been touched.
    /// </summary>
    private const string CurrentXidSql = "SELECT pg_snapshot_xmax(pg_current_snapshot())::text::bigint";

    private PostgreSqlContainer? _postgres;
    private long? _seededStateXid;
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

        await RelaxDurabilityAsync();

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
    /// Turns off crash durability on the test Postgres. This is the single biggest lever on the
    /// integration suite's wall clock and it costs nothing we want: the database is a throwaway
    /// container (Testcontainers locally, a service container in CI) that is destroyed at the end of
    /// the run, so surviving a power cut has no value — and every one of the ~790 per-test Respawn
    /// wipes pays for that durability.
    ///
    /// <para>
    /// Measured, mean per-test <c>ResetDatabaseAsync</c> against a plain <c>postgres:16-alpine</c>
    /// service container (the CI shape): <b>177 ms with defaults, 50 ms with fsync off</b>.
    /// <c>synchronous_commit = off</c> alone was worth almost nothing (162 ms) — the cost is the
    /// wipe's own fsyncs, not the commit's WAL flush — which is why this sets <c>fsync</c> and not
    /// just the cheap session-level knob.
    /// </para>
    /// <para>
    /// Applied over SQL rather than as a container flag on purpose: it then covers BOTH hosts, the
    /// Testcontainers one and the CI service container, which GitHub Actions gives no way to pass a
    /// command to. All three settings are SIGHUP-level, so <c>pg_reload_conf()</c> is enough.
    /// Non-fatal: a server where we are not superuser keeps its defaults and stays correct, only
    /// slower.
    /// </para>
    /// </summary>
    private async Task RelaxDurabilityAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                ALTER SYSTEM SET fsync = off;
                ALTER SYSTEM SET full_page_writes = off;
                ALTER SYSTEM SET synchronous_commit = off;
                SELECT pg_reload_conf();
                """,
                connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException)
        {
            // Not superuser on this server: keep the defaults. Correctness is unaffected.
        }
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

    /// <summary>
    /// Wipes every non-ignored table. Also drops the "seeded state" marker, because after this the
    /// database no longer holds the canonical seed (see <see cref="IsSeedIntactAsync"/>).
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        _seededStateXid = null;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    /// <summary>
    /// True when the database is byte-for-byte the state the last default seed left behind — i.e.
    /// NOTHING has written to this Postgres instance since <see cref="MarkSeededAsync"/> ran.
    ///
    /// <para>
    /// How it knows: Postgres assigns a real transaction id only to a transaction that WRITES;
    /// read-only transactions get a virtual one and never advance the counter. So if
    /// <c>pg_snapshot_xmax(pg_current_snapshot())</c> — the next xid the cluster will hand out —
    /// is unchanged since the seed, no INSERT/UPDATE/DELETE has been committed *or even attempted*
    /// (a rolled-back write burns its xid too) anywhere in the cluster. The check is therefore
    /// conservative in the safe direction: anything ambiguous (an autovacuum, a write in another
    /// database) reads as "dirty" and buys a full wipe + reseed.
    /// </para>
    /// <para>
    /// This is what lets a read-only test skip both the Respawn wipe and the reseed: the previous
    /// test already left exactly the rows it would have re-created. Honest size of the prize,
    /// measured over a full run: 71 of the 789 resets are skipped — nearly every test writes
    /// something — so this is worth a few seconds, not minutes. The minutes come from
    /// <see cref="RelaxDurabilityAsync"/>.
    /// </para>
    /// </summary>
    public async Task<bool> IsSeedIntactAsync()
    {
        if (_seededStateXid is null)
        {
            return false;
        }

        return await ReadCurrentXidAsync() == _seededStateXid;
    }

    /// <summary>
    /// Records that the database now holds the canonical default seed and nothing else. Call ONLY
    /// after <see cref="ResetDatabaseAsync"/> + the *default* seed: a class that seeds extra rows of
    /// its own must not mark, or the next class would reuse data it never asked for.
    /// </summary>
    public async Task MarkSeededAsync() => _seededStateXid = await ReadCurrentXidAsync();

    private async Task<long> ReadCurrentXidAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(CurrentXidSql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
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
