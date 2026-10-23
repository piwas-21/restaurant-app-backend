using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed partial class CustomerNotificationOutboxTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);

    [Fact]
    public async Task Unwatched_and_wrong_kind_subjects_remain_inert_but_exact_interest_captures()
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        Watch(db, "reservation", order.Id);
        await db.SaveChangesAsync();
        order.Status = OrderStatus.Confirmed;
        await db.SaveChangesAsync();
        Assert.Empty(await Events(db).ToListAsync());
        Watch(db, "order", order.Id);
        await db.SaveChangesAsync();
        order.Status = OrderStatus.Ready;
        await db.SaveChangesAsync();
        var hint = await Events(db).SingleAsync();
        Assert.Equal("order", hint.SubjectKind);
        Assert.Equal(order.Id, hint.SubjectId);
        Assert.NotEqual(Guid.Empty, hint.EventId);
        Assert.Equal(DateTimeKind.Utc, hint.OccurredAt.Kind);
    }

    [Theory]
    [InlineData("status", "'Confirmed'")]
    [InlineData("payment_status", "'Paid'")]
    [InlineData("effective_preparation_minutes", "25")]
    [InlineData("expected_ready_at", "'2030-05-17T18:30:00Z'")]
    [InlineData("estimated_delivery_time", "'2030-05-17T19:00:00Z'")]
    [InlineData("actual_delivery_time", "'2030-05-17T19:05:00Z'")]
    public async Task Direct_order_writes_capture_once_and_exact_replays_do_not_capture(string column, string value)
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        Watch(db, "order", order.Id);
        await db.SaveChangesAsync();
        // The test's column/value pairs are fixed InlineData, not application input.
        var sql = $"UPDATE orders SET {column} = {value} WHERE id = @id";
        await ExecuteAsync(sql, order.Id);
        var original = await Events(db).SingleAsync();
        await ExecuteAsync(sql, order.Id);
        var replay = await Events(db).SingleAsync();
        Assert.Equal(original.EventId, replay.EventId);
        Assert.Equal(original.OccurredAt, replay.OccurredAt);
    }

    [Theory]
    [InlineData("status", "1")]
    [InlineData("reservation_date", "'2030-05-18T00:00:00Z'")]
    [InlineData("start_time", "'19 hours'::interval")]
    [InlineData("end_time", "'22 hours'::interval")]
    [InlineData("number_of_guests", "3")]
    public async Task Direct_booking_changes_capture_once_without_copying_booking_contents(string column, string value)
    {
        await using var db = fixture.CreateContext();
        var booking = await SeedBookingAsync(db);
        Watch(db, "reservation", booking.Id);
        await db.SaveChangesAsync();
        var sql = $"UPDATE \"Reservations\" SET {column} = {value} WHERE id = @id";
        await ExecuteAsync(sql, booking.Id);
        var hint = await Events(db).SingleAsync();
        Assert.Equal("reservation", hint.SubjectKind);
        Assert.Equal(booking.Id, hint.SubjectId);
        await ExecuteAsync(sql, booking.Id);
        Assert.Equal(hint.EventId, (await Events(db).SingleAsync()).EventId);
    }

    [Fact]
    public async Task Contacts_staff_notes_and_revision_only_updates_do_not_create_hints()
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        var booking = await SeedBookingAsync(db);
        Watch(db, "order", order.Id); Watch(db, "reservation", booking.Id);
        await db.SaveChangesAsync();
        order.CustomerName = "Erased"; order.CustomerEmail = null; order.Notes = "Private note";
        booking.CustomerName = "Erased"; booking.CustomerEmail = "[erased]"; booking.Notes = "Staff only";
        await db.SaveChangesAsync();
        await ExecuteAsync("UPDATE \"Reservations\" SET revision = gen_random_uuid() WHERE id = @id", booking.Id);
        Assert.Empty(await Events(db).ToListAsync());
    }

    [Fact]
    public async Task Rollback_removes_both_the_business_change_and_its_hint()
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        Watch(db, "order", order.Id); await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            order.Status = OrderStatus.Ready; await db.SaveChangesAsync();
            Assert.Single(await Events(db).ToListAsync());
            await transaction.RollbackAsync();
        }
        await using var fresh = fixture.CreateContext();
        Assert.Empty(await Events(fresh).ToListAsync());
        Assert.Equal(OrderStatus.Pending, await fresh.Orders.Select(row => row.Status).SingleAsync());
    }

    [Fact]
    public async Task A_new_reader_sees_only_committed_hints_with_stable_event_identity()
    {
        await using var writer = fixture.CreateContext();
        var order = await SeedOrderAsync(writer);
        Watch(writer, "order", order.Id); await writer.SaveChangesAsync();
        Guid eventId;
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            order.Status = OrderStatus.Ready; await writer.SaveChangesAsync();
            eventId = (await Events(writer).SingleAsync()).EventId;
            await using var observer = fixture.CreateContext();
            Assert.Empty(await Events(observer).ToListAsync());
            await transaction.CommitAsync();
        }
        await using var restarted = fixture.CreateContext();
        Assert.Equal(eventId, (await Events(restarted).SingleAsync()).EventId);
        await ExecuteAsync("UPDATE orders SET status = 'Completed' WHERE id = @id", order.Id);
        Assert.Equal(2, await Events(restarted).Select(row => row.EventId).Distinct().CountAsync());
    }

    private static IQueryable<CustomerNotificationEvent> Events(ApplicationDbContext db) =>
        db.Set<CustomerNotificationEvent>().AsNoTracking();

    private static void Watch(ApplicationDbContext db, string kind, Guid id) =>
        db.Add(new CustomerNotificationWatch { SubjectKind = kind, SubjectId = id });

    private static async Task<Order> SeedOrderAsync(ApplicationDbContext db)
    {
        var id = Guid.NewGuid();
        await HistoricalOrderSeed.InsertAsync(db, id, id.ToString("N")[..20], DateTime.UtcNow, "outbox-test");
        return await db.Orders.SingleAsync(row => row.Id == id);
    }

    private static async Task<Reservation> SeedBookingAsync(ApplicationDbContext db, Guid? id = null)
    {
        var table = new Table { TableNumber = "OUTBOX-A", MaxGuests = 4, CreatedBy = "test" };
        db.Tables.Add(table); await db.SaveChangesAsync();
        var booking = new Reservation
        {
            Id = id ?? Guid.NewGuid(),
            TableId = table.Id,
            CustomerName = "Guest",
            CustomerEmail = "guest@example.test",
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            NumberOfGuests = 2,
            CreatedBy = "test"
        };
        db.Add(booking); await db.SaveChangesAsync();
        return booking;
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
}
