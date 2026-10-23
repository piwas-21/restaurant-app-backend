using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class CustomerNotificationOutboxMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Previous = "20261023085828_" + nameof(AddCustomerTablePlanOperations);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);

    [Fact]
    public async Task Upgrade_does_not_backfill_notifications_and_empty_downgrade_preserves_business_rows()
    {
        await using var db = fixture.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var orderId = Guid.NewGuid();
        await HistoricalOrderSeed.InsertAsync(db, orderId, "OUTBOX-LEGACY", DateTime.UtcNow, "test");
        await migrator.MigrateAsync();
        Assert.Empty(await db.Set<CustomerNotificationEvent>().ToListAsync());
        Assert.Empty(await db.Set<CustomerNotificationWatch>().ToListAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET status = 'Ready' WHERE id = {orderId}");
        Assert.Empty(await db.Set<CustomerNotificationEvent>().ToListAsync());
        await migrator.MigrateAsync(Previous);
        Assert.Equal(orderId, await db.Orders.Select(row => row.Id).SingleAsync());
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(TestDatabaseCluster.CurrentSchemaMigration, (await db.Database.GetAppliedMigrationsAsync()).Last());
        // The just-reinstalled trigger, rather than the template's old copy, must capture.
        db.Add(new CustomerNotificationWatch { SubjectKind = "order", SubjectId = orderId });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET status = 'Completed' WHERE id = {orderId}");
        Assert.Equal(orderId, (await db.Set<CustomerNotificationEvent>().SingleAsync()).SubjectId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_interest_with_or_without_events_refuses_downgrade_before_disabling_capture(bool hasEvent)
    {
        await using var db = fixture.CreateContext();
        var id = Guid.NewGuid();
        await HistoricalOrderSeed.InsertAsync(db, id, "OUTBOX-GUARD", DateTime.UtcNow, "test");
        db.Add(new CustomerNotificationWatch { SubjectKind = "order", SubjectId = id }); await db.SaveChangesAsync();
        if (hasEvent)
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET status = 'Ready' WHERE id = {id}");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Contains("withdrawn", failure.MessageText, StringComparison.Ordinal);
        Assert.Single(await db.Set<CustomerNotificationWatch>().AsNoTracking().ToListAsync());
        Assert.Equal(hasEvent ? 1 : 0, await db.Set<CustomerNotificationEvent>().CountAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET status = 'Completed' WHERE id = {id}");
        Assert.Equal(hasEvent ? 2 : 1, await db.Set<CustomerNotificationEvent>().CountAsync());
    }

    [Theory]
    [InlineData("staff", "11111111-2222-4333-8444-555555555555")]
    [InlineData("order", "00000000-0000-0000-0000-000000000000")]
    [InlineData("reservation", "00000000-0000-0000-0000-000000000000")]
    public async Task Storage_rejects_unknown_subjects_and_empty_ids(string kind, string id)
    {
        await using var db = fixture.CreateContext();
        db.Add(new CustomerNotificationWatch { SubjectKind = kind, SubjectId = Guid.Parse(id) });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        Assert.Equal("ck_customer_notification_watches_subject", postgres.ConstraintName);
    }

    [Fact]
    public async Task Storage_rejects_duplicate_interests_and_orphan_events()
    {
        await using var db = fixture.CreateContext();
        var id = Guid.NewGuid();
        db.Add(new CustomerNotificationWatch { SubjectKind = "order", SubjectId = id }); await db.SaveChangesAsync();
        await using var duplicate = fixture.CreateContext();
        duplicate.Add(new CustomerNotificationWatch { SubjectKind = "order", SubjectId = id });
        var duplicateFailure = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicateFailure.InnerException).SqlState);
        await using var orphan = fixture.CreateContext();
        orphan.Add(new CustomerNotificationEvent { SubjectKind = "reservation", SubjectId = id });
        var orphanFailure = await Assert.ThrowsAsync<DbUpdateException>(() => orphan.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(orphanFailure.InnerException).SqlState);
    }

    [Fact]
    public async Task In_flight_order_capture_refuses_downgrade_without_waiting_or_losing_its_hint()
    {
        await using var db = fixture.CreateContext();
        var id = Guid.NewGuid();
        await HistoricalOrderSeed.InsertAsync(db, id, "OUTBOX-LOCK", DateTime.UtcNow, "test");
        db.Add(new CustomerNotificationWatch { SubjectKind = "order", SubjectId = id });
        await db.SaveChangesAsync();
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await using var update = new NpgsqlCommand("UPDATE orders SET status = 'Ready' WHERE id = @id", writer, transaction);
        update.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await update.ExecuteNonQueryAsync());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        await transaction.CommitAsync();
        Assert.Equal(id, (await db.Set<CustomerNotificationEvent>().AsNoTracking().SingleAsync()).SubjectId);
        Assert.Equal(TestDatabaseCluster.CurrentSchemaMigration, (await db.Database.GetAppliedMigrationsAsync()).Last());
    }
}
