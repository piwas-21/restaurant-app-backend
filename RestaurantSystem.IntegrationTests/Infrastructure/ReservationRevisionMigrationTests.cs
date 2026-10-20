using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class ReservationRevisionMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Previous = "20261020173728_" + nameof(AddCustomerReservationOperations);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);
    [Fact]
    public async Task Upgrade_backfills_distinct_nonempty_revisions_without_changing_booking_data_and_empty_rollback_preserves_bookings()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        try
        {
            var table = new Table { TableNumber = "REV-A", MaxGuests = 4, CreatedBy = "test" };
            context.Tables.Add(table); await context.SaveChangesAsync();
            await migrator.MigrateAsync(Previous);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            // Historical schema must be seeded without materializing the current revision property.
            await using var seed = new NpgsqlCommand("""
                INSERT INTO "Reservations" (id,table_id,customer_name,customer_email,reservation_date,start_time,end_time,
                    number_of_guests,status,created_by,created_at) VALUES
                    ('11111111-2222-4333-8444-666666666666',@table,'Existing A','a@example.test','2030-05-17','18:00','20:00',2,1,'test',now()),
                    ('11111111-2222-4333-8444-777777777777',@table,'Existing B','b@example.test','2030-05-18','18:00','20:00',3,0,'test',now());
                """, connection);
            seed.Parameters.AddWithValue("table", table.Id);
            await seed.ExecuteNonQueryAsync();
            await migrator.MigrateAsync();
            var bookings = await context.Reservations.AsNoTracking().OrderBy(row => row.CustomerName).ToListAsync();
            Assert.Equal(new[] { "Existing A", "Existing B" }, bookings.Select(row => row.CustomerName));
            Assert.All(bookings, row => Assert.NotEqual(Guid.Empty, row.Revision));
            Assert.Equal(2, bookings.Select(row => row.Revision).Distinct().Count());
            // Exercise the trigger installed by THIS upgrade, not the template's earlier copy.
            await context.Reservations.Where(row => row.Id == bookings[0].Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerPhone, "Updated"));
            Assert.NotEqual(bookings[0].Revision, await context.Reservations.AsNoTracking()
                .Where(row => row.Id == bookings[0].Id).Select(row => row.Revision).SingleAsync());
            await migrator.MigrateAsync(Previous);
            Assert.Empty(await context.Set<CustomerReservationOperation>().AsNoTracking().ToListAsync());
            await using var check = new NpgsqlCommand("SELECT count(*) FROM \"Reservations\"", connection);
            Assert.Equal(2L, await check.ExecuteScalarAsync());
            await migrator.MigrateAsync();
            Assert.False(context.Database.HasPendingModelChanges());
        }
        finally { await migrator.MigrateAsync(); }
    }
    [Fact]
    public async Task Generated_revision_is_read_back_on_EF_parent_insert_and_update_and_raw_SQL_cannot_reuse_it()
    {
        await using var db = fixture.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous); await migrator.MigrateAsync();
        var table = new Table { TableNumber = "REV-EF", MaxGuests = 4, CreatedBy = "test" };
        db.Tables.Add(table); await db.SaveChangesAsync();
        var row = new Reservation
        {
            TableId = table.Id,
            CustomerName = "Revision test",
            CustomerEmail = "revision@example.test",
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            NumberOfGuests = 2,
            CreatedBy = "test"
        };
        db.Reservations.Add(row); await db.SaveChangesAsync(); var inserted = row.Revision;
        Assert.NotEqual(Guid.Empty, inserted);
        row.CustomerPhone = "Changed"; await db.SaveChangesAsync(); var updated = row.Revision;
        Assert.NotEqual(inserted, updated);
        Assert.Equal(updated, await db.Reservations.AsNoTracking().Select(value => value.Revision).SingleAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Reservations\" SET revision = {inserted} WHERE id = {row.Id}");
        var fresh = await db.Reservations.AsNoTracking().Select(value => value.Revision).SingleAsync();
        Assert.NotEqual(inserted, fresh); Assert.NotEqual(updated, fresh);
    }
    [Fact]
    public async Task Combined_table_insert_move_and_delete_rotate_every_affected_booking_revision()
    {
        await using var db = fixture.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous); await migrator.MigrateAsync();
        var table = new Table { TableNumber = "REV-CHILD", MaxGuests = 4, CreatedBy = "test" };
        db.Tables.Add(table); await db.SaveChangesAsync();
        Reservation Booking() => new()
        {
            TableId = table.Id,
            CustomerName = "Unchanged",
            CustomerEmail = "revision@example.test",
            ReservationDate = new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            NumberOfGuests = 2,
            CreatedBy = "test"
        };
        var first = Booking(); var second = Booking(); db.Reservations.AddRange(first, second); await db.SaveChangesAsync();
        async Task<Guid> Revision(Guid id) => await db.Reservations.AsNoTracking().Where(row => row.Id == id).Select(row => row.Revision).SingleAsync();
        var firstBefore = await Revision(first.Id); var secondBefore = await Revision(second.Id);
        var child = new ReservationTable { ReservationId = first.Id, TableId = table.Id, CreatedBy = "test" };
        db.Set<ReservationTable>().Add(child); await db.SaveChangesAsync();
        var firstInserted = await Revision(first.Id);
        Assert.NotEqual(firstBefore, firstInserted); Assert.Equal(secondBefore, await Revision(second.Id));
        child.ReservationId = second.Id; await db.SaveChangesAsync();
        var firstMoved = await Revision(first.Id); var secondMoved = await Revision(second.Id);
        Assert.NotEqual(firstInserted, firstMoved); Assert.NotEqual(secondBefore, secondMoved);
        db.Set<ReservationTable>().Remove(child); await db.SaveChangesAsync();
        Assert.Equal(firstMoved, await Revision(first.Id)); Assert.NotEqual(secondMoved, await Revision(second.Id));
        Assert.All(await db.Reservations.AsNoTracking().ToListAsync(), row =>
        { Assert.Equal("Unchanged", row.CustomerName); Assert.Equal(2, row.NumberOfGuests); });
    }
}
