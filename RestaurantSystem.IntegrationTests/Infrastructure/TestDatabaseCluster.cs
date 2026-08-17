using Npgsql;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// The one Postgres the whole assembly shares, and the source of the per-lane databases that let
/// the DB-backed collections run in PARALLEL.
///
/// <para>
/// Shape: the cluster is bootstrapped ONCE (container or the CI service container, durability
/// relaxed, migrations applied). That migrated database is then used only as a <c>TEMPLATE</c> —
/// every <see cref="DatabaseFixture"/> asks for its own lane database with
/// <c>CREATE DATABASE … TEMPLATE …</c>, a file copy that costs a fraction of re-running migrations.
/// Nothing ever connects to the template again, which is what keeps <c>CREATE DATABASE</c> legal
/// while other lanes are mid-test.
/// </para>
/// <para>
/// Why a database per lane and not a schema or a shared one: every test wipes its database with
/// Respawn. Two lanes on one database would have one lane's wipe delete the rows another lane had
/// just seeded, mid-test. The lane database IS the isolation boundary.
/// </para>
/// <para>
/// The container is deliberately never disposed. Lanes start lazily and a collection can finish
/// before another begins, so there is no safe "last one out" moment; Testcontainers' Ryuk reaper
/// removes it when the test process exits. In CI there is no container at all — the connection
/// comes from <c>INTEGRATION_TESTS_DB_CONNECTION</c>.
/// </para>
/// </summary>
internal static class TestDatabaseCluster
{
    /// <summary>
    /// When set, the tests connect to this Postgres instead of starting a Testcontainers
    /// container. Used in CI, where the workflow declares a postgres service container and
    /// exports its URL into this variable.
    /// </summary>
    private const string ExternalConnectionEnv = "INTEGRATION_TESTS_DB_CONNECTION";

    /// <summary>
    /// Bound per lane so four lanes cannot exhaust Postgres' default 100 connections: each lane
    /// runs its tests serially, so a handful of connections is all it can ever use.
    /// </summary>
    private const int MaxPoolSizePerLane = 10;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static string? _templateConnectionString;
    private static int _laneOrdinal;

    /// <summary>
    /// Creates a private database for one lane, seeded by copying the migrated template, and
    /// returns its connection string. Serialised: <c>CREATE DATABASE</c> is cheap but two of them
    /// from the same template at the same instant is not worth the race.
    /// </summary>
    public static async Task<string> CreateLaneDatabaseAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var template = await EnsureTemplateAsync();
            var templateName = new NpgsqlConnectionStringBuilder(template).Database!;
            var lane = $"lane_{Interlocked.Increment(ref _laneOrdinal)}";

            await using (var admin = new NpgsqlConnection(ConnectionTo(template, "postgres")))
            {
                await admin.OpenAsync();
                // Separate round trips on purpose: CREATE DATABASE cannot run inside the implicit
                // transaction Npgsql would wrap a multi-statement batch in.
                await ExecuteAsync(admin, $"""DROP DATABASE IF EXISTS "{lane}" WITH (FORCE)""");
                await ExecuteAsync(admin, $"""CREATE DATABASE "{lane}" TEMPLATE "{templateName}" """);
            }

            return ConnectionTo(template, lane);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string> EnsureTemplateAsync()
    {
        if (_templateConnectionString is not null)
        {
            return _templateConnectionString;
        }

        var external = Environment.GetEnvironmentVariable(ExternalConnectionEnv);
        string connectionString;
        if (!string.IsNullOrWhiteSpace(external))
        {
            connectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("restaurant_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();

            await _container.StartAsync();
            connectionString = _container.GetConnectionString();
        }

        await RelaxDurabilityAsync(connectionString);
        await MigrateAsync(connectionString);

        // Leave no session on the template: CREATE DATABASE … TEMPLATE refuses while one exists.
        NpgsqlConnection.ClearAllPools();

        _templateConnectionString = connectionString;
        return connectionString;
    }

    private static async Task MigrateAsync(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.EnableDynamicJson();
        await using var dataSource = builder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
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
    /// command to. All three settings are SIGHUP-level, so <c>pg_reload_conf()</c> is enough, and
    /// they are cluster-wide, so every lane database inherits them. Non-fatal: a server where we are
    /// not superuser keeps its defaults and stays correct, only slower.
    /// </para>
    /// </summary>
    private static async Task RelaxDurabilityAsync(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                """
                ALTER SYSTEM SET fsync = off;
                ALTER SYSTEM SET full_page_writes = off;
                ALTER SYSTEM SET synchronous_commit = off;
                SELECT pg_reload_conf();
                """);
        }
        catch (PostgresException)
        {
            // Not superuser on this server: keep the defaults. Correctness is unaffected.
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ConnectionTo(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database,
            MaxPoolSize = MaxPoolSizePerLane,
        }.ConnectionString;
}
