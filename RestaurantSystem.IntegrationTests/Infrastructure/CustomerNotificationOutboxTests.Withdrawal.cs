using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public sealed partial class CustomerNotificationOutboxTests
{
    [Theory]
    [InlineData("order")]
    [InlineData("reservation")]
    public async Task Withdrawing_interest_erases_pending_hints_and_stops_future_capture(string kind)
    {
        await using var db = fixture.CreateContext();
        var id = kind == "order" ? (await SeedOrderAsync(db)).Id : (await SeedBookingAsync(db)).Id;
        Watch(db, kind, id); await db.SaveChangesAsync();
        var sql = kind == "order"
            ? "UPDATE orders SET status = 'Ready' WHERE id = @id"
            : "UPDATE \"Reservations\" SET status = 1 WHERE id = @id";
        await ExecuteAsync(sql, id);
        Assert.Single(await Events(db).ToListAsync());
        await db.Set<CustomerNotificationWatch>().ExecuteDeleteAsync();
        Assert.Empty(await Events(db).ToListAsync());
        await ExecuteAsync(kind == "order"
            ? "UPDATE orders SET status = 'Completed' WHERE id = @id"
            : "UPDATE \"Reservations\" SET status = 3 WHERE id = @id", id);
        Assert.Empty(await Events(db).ToListAsync());
    }

    [Theory]
    [InlineData("order", true)]
    [InlineData("reservation", false)]
    public async Task Subject_deletion_withdraws_capture_and_pending_hints(string kind, bool softDelete)
    {
        await using var db = fixture.CreateContext();
        var id = kind == "order" ? (await SeedOrderAsync(db)).Id : (await SeedBookingAsync(db)).Id;
        Watch(db, kind, id); await db.SaveChangesAsync();
        await ExecuteAsync(kind == "order"
            ? "UPDATE orders SET status = 'Ready' WHERE id = @id"
            : "UPDATE \"Reservations\" SET status = 1 WHERE id = @id", id);
        Assert.Single(await Events(db).ToListAsync());
        await ExecuteAsync(softDelete ? "UPDATE orders SET is_deleted = true WHERE id = @id"
            : kind == "order" ? "DELETE FROM orders WHERE id = @id"
            : "DELETE FROM \"Reservations\" WHERE id = @id", id);
        Assert.Empty(await Events(db).ToListAsync());
        Assert.Empty(await db.Set<CustomerNotificationWatch>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Protected_order_history_still_refuses_hard_deletion_without_losing_queued_hints()
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        Watch(db, "order", order.Id); await db.SaveChangesAsync();
        await ExecuteAsync("UPDATE orders SET status = 'Ready' WHERE id = @id", order.Id);
        var hint = await Events(db).SingleAsync();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM orders WHERE id = @id", order.Id));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, failure.SqlState);
        Assert.Equal("fk_order_changes_orders_order_id", failure.ConstraintName);
        Assert.Equal(hint.EventId, (await Events(db).SingleAsync()).EventId);
        Assert.Single(await db.Set<CustomerNotificationWatch>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Capture_failure_rolls_back_the_business_change_instead_of_losing_its_hint()
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        Watch(db, "order", order.Id); await db.SaveChangesAsync();
        // A hostile storage constraint models an outbox write failure after the order UPDATE.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE customer_notification_events ADD CONSTRAINT notification_test_refuse CHECK (subject_kind <> 'order')
            """);
        try
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync("UPDATE orders SET status = 'Ready' WHERE id = @id", order.Id));
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            Assert.Equal("notification_test_refuse", failure.ConstraintName);
            Assert.Empty(await Events(db).ToListAsync());
            Assert.Equal("Pending", await db.Orders.Select(row => row.Status.ToString()).SingleAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE customer_notification_events DROP CONSTRAINT notification_test_refuse");
        }
    }

    [Fact]
    public async Task Event_projection_is_opaque_and_queue_and_watch_lookups_have_indexes()
    {
        await using var db = fixture.CreateContext();
        var properties = db.Model.FindEntityType(typeof(CustomerNotificationEvent))!.GetProperties();
        Assert.Equal(new[] { "EventId", "OccurredAt", "SubjectId", "SubjectKind" }, properties.Select(row => row.Name));
        var indexes = db.Model.FindEntityType(typeof(CustomerNotificationEvent))!.GetIndexes().ToArray();
        Assert.Contains(indexes, index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { "OccurredAt", "EventId" }));
        Assert.Contains(indexes, index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { "SubjectKind", "SubjectId" }));
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
