using System.Net;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditOperationTests
{
    [Theory]
    [InlineData(18.25, 20.25, false)]
    [InlineData(18.25, 21.25, false)]
    [InlineData(18, 20, true)]
    public async Task Contact_only_edits_preserve_agreed_off_grid_duration_or_changed_hours_but_shape_edits_need_a_current_offer(
        double start, double end, bool closed)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.StartTime, TimeSpan.FromHours(start))
            .SetProperty(row => row.EndTime, TimeSpan.FromHours(end)));
        if (closed) await db.WorkingHours.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsClosed, true));
        var contact = Request() with { StartTime = TimeSpan.FromHours(start), EndTime = TimeSpan.FromHours(end) };
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("prepare", contact with { NumberOfGuests = 5 })).StatusCode);
        var (request, review) = await PrepareAsync(contact);
        Assert.Equal(ReservationStatus.Confirmed, review.Proposed.Status);
        Assert.Equal("Applied", (await DecideAsync("submit", request)).GetProperty("outcome").GetString());
        var saved = await db.Reservations.AsNoTracking().SingleAsync();
        Assert.Equal(ReservationStatus.Confirmed, saved.Status); Assert.Equal("changed@example.test", saved.CustomerEmail);
        Assert.Equal(TimeSpan.FromHours(start), saved.StartTime); Assert.Equal(TimeSpan.FromHours(end), saved.EndTime);
        Assert.Equal(_first, saved.TableId); Assert.Equal(_second, (await db.Set<ReservationTable>().SingleAsync()).TableId);
        if (closed) await db.WorkingHours.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsClosed, false));
        var (fresh, changed) = await PrepareAsync(Request() with { NumberOfGuests = 5 });
        Assert.Equal(ReservationStatus.Pending, changed.Proposed.Status);
        Assert.Equal("Applied", (await DecideAsync("submit", fresh)).GetProperty("outcome").GetString());
        Assert.Equal(ReservationStatus.Pending, (await db.Reservations.AsNoTracking().SingleAsync()).Status);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Contact_only_off_grid_changes_still_recheck_whole_arrangement_capacity_and_conflicts(bool conflict)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.StartTime, TimeSpan.FromHours(18.25))
            .SetProperty(row => row.EndTime, TimeSpan.FromHours(21.25)));
        var (request, _) = await PrepareAsync(Request() with { StartTime = TimeSpan.FromHours(18.25), EndTime = TimeSpan.FromHours(21.25) });
        var revision = (await db.Reservations.AsNoTracking().SingleAsync()).Revision;
        if (conflict)
        { var other = Booking(_second, null); other.StartTime = TimeSpan.FromHours(19); other.EndTime = TimeSpan.FromHours(20); db.Reservations.Add(other); await db.SaveChangesAsync(); }
        else await db.Tables.Where(row => row.Id == _first).ExecuteUpdateAsync(update => update.SetProperty(row => row.MaxGuests, 1));
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("submit", request)).StatusCode);
        var original = await db.Reservations.AsNoTracking().SingleAsync(row => row.Id == _booking);
        Assert.Equal(revision, original.Revision); Assert.Equal("editor@example.test", original.CustomerEmail);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
}
