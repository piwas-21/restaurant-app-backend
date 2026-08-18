using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// One test LANE: its own database, its own connection pool and its own shared test host.
///
/// <para>
/// There is one instance of this per DB-backed xUnit collection (see <c>DatabaseCollections.cs</c>),
/// and xUnit runs collections in parallel — so the lanes really do run at the same time. The
/// database is private to the lane (<see cref="TestDatabaseCluster.CreateLaneDatabaseAsync"/>),
/// which is what makes that safe: the per-test Respawn wipe below deletes everything in the
/// database it runs against, so two lanes sharing one would destroy each other's rows mid-test.
/// </para>
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// The next transaction id the cluster will assign. Only a WRITING transaction consumes one,
    /// so an unchanged value proves the database has not been touched.
    /// </summary>
    private const string CurrentXidSql = "SELECT pg_snapshot_xmax(pg_current_snapshot())::text::bigint";

    private long? _seededStateXid;
    private Respawner _respawner = null!;
    private readonly Lock _sharedFactoryLock = new();
    private TestWebApplicationFactory? _sharedFactory;
    // Shared DataSource (one connection pool for this lane). Without this, CreateContext() built a
    // fresh DataSource per call and each got its own pool — once enough tests ran, Postgres hit
    // max_connections (53300).
    private NpgsqlDataSource _dataSource = null!;

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = await TestDatabaseCluster.CreateLaneDatabaseAsync();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();

        // No migration here: the lane database was created as a copy of the already-migrated
        // template, schema and all.
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
    /// ONE test host per lane, shared by every <see cref="IntegrationTestBase"/> class in that lane
    /// that neither overrides DI nor opts out via <c>RequiresIsolatedHost</c>.
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
    /// Wipes every non-ignored table in THIS lane's database. Also drops the "seeded state" marker,
    /// because after this the database no longer holds the canonical seed (see
    /// <see cref="IsSeedIntactAsync"/>).
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
    /// Honest size of the prize: it was worth 71 skipped resets of 789 when the suite was serial,
    /// i.e. seconds. Now that lanes run in parallel the counter is cluster-wide, so a sibling lane's
    /// write reads as "dirty" here and the skip almost never fires. It is kept because it is free
    /// and stays correct in both shapes — never because it is load-bearing. The minutes come from
    /// <see cref="TestDatabaseCluster"/>'s durability settings and from the lanes themselves.
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
    }
}
