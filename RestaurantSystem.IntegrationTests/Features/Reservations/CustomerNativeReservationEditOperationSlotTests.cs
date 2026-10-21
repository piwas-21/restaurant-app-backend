using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditOperationTests
{
    [Theory]
    [InlineData("capacity")]
    [InlineData("inactive-primary")]
    [InlineData("inactive-child")]
    [InlineData("child-conflict")]
    [InlineData("closed")]
    public async Task Submission_rechecks_the_entire_existing_arrangement_and_never_chooses_the_spare(string mode)
    {
        Authenticate(); var (request, _) = await PrepareAsync(); await using var db = DatabaseFixture.CreateContext();
        var revision = (await db.Reservations.SingleAsync()).Revision;
        if (mode == "capacity") await db.Tables.Where(row => row.Id == _first).ExecuteUpdateAsync(update => update.SetProperty(row => row.MaxGuests, 1));
        else if (mode is "inactive-primary" or "inactive-child")
        { var id = mode == "inactive-primary" ? _first : _second; await db.Tables.Where(row => row.Id == id).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, false)); }
        else if (mode == "closed") await db.WorkingHours.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsClosed, true));
        else
        { var other = Booking(_second, null); other.StartTime = TimeSpan.FromHours(20); other.EndTime = TimeSpan.FromHours(22); db.Reservations.Add(other); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("submit", request)).StatusCode);
        var own = await db.Reservations.AsNoTracking().SingleAsync(row => row.Id == _booking);
        Assert.Equal(revision, own.Revision); Assert.Equal(_first, own.TableId); Assert.Equal(TimeSpan.FromHours(18), own.StartTime);
        Assert.Equal(_second, (await db.Set<ReservationTable>().SingleAsync()).TableId);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_started_original_or_proposed_booking_cannot_be_changed(bool originalStarted)
    {
        Authenticate();
        if (!originalStarted)
        {
            await using var current = DatabaseFixture.CreateContext();
            await current.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.StartTime, TimeSpan.FromHours(21))
                .SetProperty(row => row.EndTime, TimeSpan.FromHours(23)));
        }
        var (request, _) = await PrepareAsync();
        _clock.SetupGet(value => value.Now).Returns(new DateTimeOffset(2030, 5, 17, originalStarted ? 18 : 20, 0, 0, TimeSpan.FromHours(2)));
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("submit", request)).StatusCode);
        await using var db = DatabaseFixture.CreateContext(); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Fact]
    public async Task Canonical_edit_decisions_do_not_reapply_after_restaurant_changes_or_booking_removal()
    {
        Authenticate(); var (request, review) = await PrepareAsync(); var accepted = await DecideAsync("submit", request);
        await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Confirmed)
            .SetProperty(row => row.CustomerPhone, "Staff correction"));
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("submit", request)).GetRawText());
        Assert.Equal("Staff correction", (await db.Reservations.AsNoTracking().SingleAsync()).CustomerPhone);
        await db.Reservations.ExecuteDeleteAsync();
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("recover", review.Proof)).GetRawText());
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("cancel-attempt", review.Proof)).GetRawText()); Assert.Equal(1, _mailCalls);
    }
}
