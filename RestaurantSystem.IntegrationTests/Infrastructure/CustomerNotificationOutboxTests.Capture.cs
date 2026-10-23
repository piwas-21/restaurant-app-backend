using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public sealed partial class CustomerNotificationOutboxTests
{
    [Fact]
    public async Task Ef_set_based_updates_capture_without_relying_on_the_stale_tracked_entity()
    {
        await using var db = fixture.CreateContext();
        var order = await SeedOrderAsync(db);
        var booking = await SeedBookingAsync(db);
        Watch(db, "order", order.Id);
        Watch(db, "reservation", booking.Id);
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Orders.Where(row => row.Id == order.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, OrderStatus.Ready)));
        Assert.Equal(1, await db.Reservations.Where(row => row.Id == booking.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Confirmed)));
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(ReservationStatus.Pending, booking.Status);
        var hints = await Events(db).ToListAsync();
        Assert.Equal(2, hints.Count);
        Assert.Contains(hints, row => row.SubjectKind == "order" && row.SubjectId == order.Id);
        Assert.Contains(hints, row => row.SubjectKind == "reservation" && row.SubjectId == booking.Id);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("reservation")]
    public async Task Interest_installed_in_the_creation_transaction_captures_the_initial_insert(string kind)
    {
        await using var db = fixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var id = Guid.NewGuid();
        Watch(db, kind, id);
        await db.SaveChangesAsync();
        if (kind == "order")
            await HistoricalOrderSeed.InsertAsync(db, id, "OUTBOX-INSERT", DateTime.UtcNow, "test");
        else
            await SeedBookingAsync(db, id);
        Assert.Equal(id, (await Events(db).SingleAsync()).SubjectId);
        await transaction.CommitAsync();
    }
}
