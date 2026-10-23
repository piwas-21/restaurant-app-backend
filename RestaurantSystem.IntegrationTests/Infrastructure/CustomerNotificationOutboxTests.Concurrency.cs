using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public sealed partial class CustomerNotificationOutboxTests
{
    [Theory]
    [InlineData("order")]
    [InlineData("reservation")]
    public async Task Withdrawal_winning_the_watch_lock_does_not_abort_a_waiting_business_update(string kind)
    {
        await using var db = fixture.CreateContext();
        var id = kind == "order" ? (await SeedOrderAsync(db)).Id : (await SeedBookingAsync(db)).Id;
        Watch(db, kind, id); await db.SaveChangesAsync();
        await using var withdrawal = new NpgsqlConnection(fixture.ConnectionString);
        await withdrawal.OpenAsync();
        await using var transaction = await withdrawal.BeginTransactionAsync();
        await using (var remove = new NpgsqlCommand("DELETE FROM customer_notification_watches WHERE subject_kind = @kind AND subject_id = @id", withdrawal, transaction))
        {
            remove.Parameters.AddWithValue("kind", kind); remove.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await remove.ExecuteNonQueryAsync());
        }
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var update = new NpgsqlCommand(kind == "order"
            ? "UPDATE orders SET status = 'Ready' WHERE id = @id"
            : "UPDATE \"Reservations\" SET status = 1 WHERE id = @id", writer);
        update.Parameters.AddWithValue("id", id);
        var write = update.ExecuteNonQueryAsync();
        try
        {
            Assert.True(await MigrationTestSafety.WaitForBlockedMigrationAsync(fixture.ConnectionString, writer.ProcessID));
            await transaction.CommitAsync();
            Assert.Equal(1, await write);
            Assert.Empty(await Events(db).ToListAsync());
        }
        finally
        {
            if (!write.IsCompleted) await transaction.RollbackAsync();
            await write;
        }
    }

    [Fact]
    public async Task Capture_winning_the_watch_lock_is_erased_by_the_waiting_withdrawal()
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        Watch(db, "order", order.Id); await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET status = 'Ready' WHERE id = {order.Id}");
        Assert.Single(await Events(db).ToListAsync());
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var remove = new NpgsqlCommand("DELETE FROM customer_notification_watches WHERE subject_kind = 'order' AND subject_id = @id", connection);
        remove.Parameters.AddWithValue("id", order.Id);
        var withdrawal = remove.ExecuteNonQueryAsync();
        try
        {
            Assert.True(await MigrationTestSafety.WaitForBlockedMigrationAsync(fixture.ConnectionString, connection.ProcessID));
            await transaction.CommitAsync();
            Assert.Equal(1, await withdrawal);
            Assert.Empty(await Events(db).ToListAsync());
        }
        finally
        {
            if (!withdrawal.IsCompleted) await transaction.RollbackAsync();
            await withdrawal;
        }
    }
}
