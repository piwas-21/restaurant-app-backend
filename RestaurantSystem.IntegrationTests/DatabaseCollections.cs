using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests;

// FOUR lanes, not one. Every DB-backed test class used to sit in a single "Database" collection,
// and xUnit never runs a collection in parallel with itself — so 796 tests that spend nearly all
// their time waiting on Postgres executed strictly one after another.
//
// Each definition below registers its OWN DatabaseFixture instance (xUnit creates one fixture per
// collection), and each of those provisions its own database from the migrated template
// (TestDatabaseCluster). That is the whole safety argument: the per-test Respawn wipe deletes
// everything in the database it is pointed at, so lanes MUST NOT share one.
//
// Classes are assigned to lanes by test count, balanced at 199 tests each. A class may be moved
// between lanes freely — the lanes have no semantics, only load.
//
// The lane count is not the concurrency: xUnit's maxParallelThreads defaults to the core count, so
// a 2-core CI runner runs two lanes at a time and simply schedules the other two behind them.

[CollectionDefinition("Database Lane 1")]
public class DatabaseLane1Collection : ICollectionFixture<DatabaseFixture>;

[CollectionDefinition("Database Lane 2")]
public class DatabaseLane2Collection : ICollectionFixture<DatabaseFixture>;

[CollectionDefinition("Database Lane 3")]
public class DatabaseLane3Collection : ICollectionFixture<DatabaseFixture>;

[CollectionDefinition("Database Lane 4")]
public class DatabaseLane4Collection : ICollectionFixture<DatabaseFixture>;
