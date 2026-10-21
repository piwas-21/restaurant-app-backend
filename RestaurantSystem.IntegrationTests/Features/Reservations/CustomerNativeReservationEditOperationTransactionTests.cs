using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditOperationTests
{
    [Fact]
    public async Task Receipt_save_failure_rolls_back_the_whole_edit_and_revision_before_any_mail()
    {
        Authenticate(); var (request, review) = await PrepareAsync(); await using var db = DatabaseFixture.CreateContext();
        var revision = (await db.Reservations.SingleAsync()).Revision; var observed = false;
        _saveBoundary.AfterReceipt = async actual =>
        {
            Assert.NotNull(actual.Database.CurrentTransaction); observed = true;
            Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
            Assert.Equal("editor@example.test", (await db.Reservations.AsNoTracking().SingleAsync()).CustomerEmail);
            throw new IOException("Injected receipt save failure.");
        };
        Assert.Equal(HttpStatusCode.InternalServerError, (await SendAsync("submit", request)).StatusCode);
        Assert.True(observed); await AssertUntouchedAsync(revision);
        Assert.Equal("Unseen", (await DecideAsync("recover", review.Proof)).GetProperty("outcome").GetString());
        Assert.Equal("Applied", (await DecideAsync("submit", request)).GetProperty("outcome").GetString()); Assert.Equal(1, _mailCalls);
    }
    [Theory]
    [InlineData("original-clock")]
    [InlineData("policy")]
    [InlineData("tenant")]
    [InlineData("zone")]
    [InlineData("currency")]
    [InlineData("revision")]
    [InlineData("capacity")]
    public async Task Authority_revision_and_capacity_are_rechecked_after_the_actual_edit_and_receipt_save(string mode)
    {
        Authenticate(); var (request, _) = await PrepareAsync(); await using var db = DatabaseFixture.CreateContext();
        var revision = (await db.Reservations.SingleAsync()).Revision; var observed = false;
        _saveBoundary.AfterReceipt = async actual =>
        {
            observed = true; Assert.NotNull(actual.Database.CurrentTransaction);
            Assert.Single(actual.ChangeTracker.Entries<CustomerReservationOperation>());
            Assert.Equal(TimeSpan.FromHours(20), await actual.Reservations.AsNoTracking().Select(row => row.StartTime).SingleAsync());
            if (mode == "original-clock") _clock.SetupGet(value => value.Now).Returns(new DateTimeOffset(2030, 5, 17, 18, 0, 0, TimeSpan.FromHours(2)));
            else if (mode == "policy") Mobile.Enabled = false;
            else if (mode == "tenant") Mobile.TenantId = Guid.NewGuid();
            else if (mode == "zone") _clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
            else if (mode == "currency") await actual.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, "EUR"));
            else if (mode == "capacity") await actual.Tables.Where(row => row.Id == _first).ExecuteUpdateAsync(update => update.SetProperty(row => row.MaxGuests, 1));
            else await actual.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerPhone, "Changed after save"));
        };
        var expected = mode is "original-clock" or "revision" or "capacity" ? HttpStatusCode.Conflict : HttpStatusCode.NotFound;
        Assert.Equal(expected, (await SendAsync("submit", request)).StatusCode); Assert.True(observed);
        await AssertUntouchedAsync(revision);
    }
    [Fact]
    public async Task Actual_mail_provider_failure_does_not_undo_or_replay_the_committed_edit()
    {
        Authenticate(); var (request, review) = await PrepareAsync();
        _email.Setup(value => value.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("Injected changed-booking provider failure."));
        var accepted = await DecideAsync("submit", request);
        Assert.Equal("Applied", accepted.GetProperty("outcome").GetString());
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("recover", review.Proof)).GetRawText());
        Assert.Equal(accepted.GetRawText(), (await DecideAsync("submit", request)).GetRawText());
        _email.Verify(value => value.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Equal(1, _mailCalls);
    }
}
