using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditReadTests
{
    [Theory]
    [InlineData("accounts")]
    [InlineData("reservations")]
    [InlineData("read-only")]
    [InlineData("disabled")]
    [InlineData("module")]
    [InlineData("currency")]
    [InlineData("role")]
    [InlineData("deleted")]
    public async Task Fresh_editor_access_refuses_withdrawn_policy_and_current_database_ownership(string mode)
    {
        Authenticate(); await ReadAsync();
        if (mode == "accounts") Mobile.EnabledCapabilities = ["reservations"];
        else if (mode == "reservations") Mobile.EnabledCapabilities = ["accounts"];
        else if (mode is "read-only" or "disabled") Mobile.Lifecycle = mode;
        else if (mode == "module") _modules.Setup(value => value.IsEnabled(ModuleIds.Reservations)).Returns(false);
        else
        {
            await using var db = DatabaseFixture.CreateContext();
            if (mode == "currency") await db.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, (string?)null));
            else if (mode == "role") await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, UserRole.Admin));
            else await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        }
        foreach (var suffix in new[] { "?schemaVersion=1", AvailabilityQuery })
            Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + suffix)).StatusCode);
    }
    [Theory]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.NoShow)]
    public async Task Terminal_bookings_have_no_edit_context_or_owned_availability(ReservationStatus status)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.Where(row => row.Id == _booking).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status));
        foreach (var suffix in new[] { "?schemaVersion=1", AvailabilityQuery })
            Assert.Equal(HttpStatusCode.Conflict, (await Client.GetAsync(Route + suffix)).StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restaurant_clock_refuses_started_or_past_bookings_even_if_the_calendar_marker_is_not_UTC_midnight(bool previousDay)
    {
        Authenticate();
        _clock.SetupGet(value => value.Now).Returns(new DateTimeOffset(2030, 5, previousDay ? 18 : 17, 18, 0, 0, TimeSpan.FromHours(2)));
        foreach (var suffix in new[] { "?schemaVersion=1", AvailabilityQuery })
            Assert.Equal(HttpStatusCode.Conflict, (await Client.GetAsync(Route + suffix)).StatusCode);
    }
    [Theory]
    [InlineData("account")]
    [InlineData("role")]
    [InlineData("capability")]
    [InlineData("tenant")]
    [InlineData("timezone")]
    [InlineData("currency")]
    [InlineData("contact-aba")]
    [InlineData("child-aba")]
    [InlineData("clock")]
    public async Task Changes_after_the_real_slot_query_cannot_publish_stale_editor_availability(string mode)
    {
        Authenticate(); var observed = false;
        _afterSlots = async () =>
        {
            _afterSlots = null; observed = true;
            if (mode == "capability") Mobile.EnabledCapabilities = ["accounts"];
            else if (mode == "tenant") Mobile.TenantId = Guid.NewGuid();
            else if (mode == "timezone") _clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
            else if (mode == "clock") _clock.SetupGet(value => value.Now).Returns(new DateTimeOffset(2030, 5, 17, 18, 0, 0, TimeSpan.FromHours(2)));
            else
            {
                await using var db = DatabaseFixture.CreateContext();
                if (mode == "account") await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                else if (mode == "role") await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, UserRole.Admin));
                else if (mode == "currency") await db.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, "EUR"));
                else if (mode == "contact-aba")
                {
                    await db.Reservations.Where(row => row.Id == _booking).ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerPhone, "Changed"));
                    await db.Reservations.Where(row => row.Id == _booking).ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerPhone, "+41000000000"));
                }
                else
                {
                    var child = new ReservationTable { ReservationId = _booking, TableId = _spare, CreatedBy = "test" };
                    db.Set<ReservationTable>().Add(child); await db.SaveChangesAsync();
                    db.Set<ReservationTable>().Remove(child); await db.SaveChangesAsync();
                }
            }
        };
        var expected = mode is "contact-aba" or "child-aba" or "clock" ? HttpStatusCode.Conflict : HttpStatusCode.NotFound;
        Assert.Equal(expected, (await Client.GetAsync(Route + AvailabilityQuery)).StatusCode);
        Assert.True(observed);
    }
}
