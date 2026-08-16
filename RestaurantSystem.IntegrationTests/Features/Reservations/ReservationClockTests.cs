using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// Reservation dates and slots are WALL-CLOCK values, so "today" and "now" have to be read on the
/// tenant's clock (backend #369, the tail of #363).
/// <para>
/// Both instants are chosen so UTC and the tenant's wall clock disagree — about the hour in the
/// first test, about the DAY in the second. The assertions are literal slot times and literal
/// status codes, never a second value computed the same way the code under test computes it.
/// </para>
/// </summary>
public class ReservationClockTests : IntegrationTestBase
{
    /// <summary>Friday 2030-05-17, the day both tests book.</summary>
    private const string BookedDay = "2030-05-17";

    private readonly MutableClock _clock = new(
        new DateTimeOffset(2030, 5, 17, 18, 15, 0, TimeSpan.Zero), "Europe/Zurich");

    public ReservationClockTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantClock>(_clock);
    }

    [Fact]
    public async Task Past_slots_are_filtered_on_the_tenant_wall_clock_not_UTC()
    {
        // 18:15 UTC is 20:15 in Zurich in May. The old code filtered against 18:15 and so still
        // offered a table for 18:30 — two hours in the past for the guest standing in Geneva.
        await SeedFridayAsync(open: new TimeSpan(11, 0, 0), close: new TimeSpan(23, 0, 0));

        var slots = await GetSlotStartsAsync(BookedDay);

        slots.Should().Equal(new[] { "20:30:00", "21:00:00" });
    }

    [Fact]
    public async Task A_venue_west_of_UTC_can_still_be_asked_for_tonight()
    {
        // 00:15 UTC on the 18th is 20:15 on the 17th in New York: UtcNow.Date was already
        // tomorrow, so "date is in the past" refused TONIGHT outright.
        _clock.Set(new DateTimeOffset(2030, 5, 18, 0, 15, 0, TimeSpan.Zero), "America/New_York");
        await SeedFridayAsync(open: new TimeSpan(11, 0, 0), close: new TimeSpan(23, 0, 0));

        var slots = await GetSlotStartsAsync(BookedDay);

        // Tonight's remaining service, not an error and not an empty list.
        slots.Should().Equal(new[] { "20:30:00", "21:00:00" });
    }

    [Fact]
    public async Task The_day_a_booking_is_refused_as_past_is_the_tenant_day()
    {
        // Still 20:15 on the 17th in New York while UTC has already turned the 18th. The pair of
        // assertions is what pins the boundary: reading UTC would refuse BOTH days on a host whose
        // clock is anywhere near this instant, and refuse NEITHER on a host far from it (which is
        // every CI runner — hence a booking for tonight alone would prove nothing here).
        _clock.Set(new DateTimeOffset(2030, 5, 18, 0, 15, 0, TimeSpan.Zero), "America/New_York");
        var tableId = await SeedFridayAsync(open: new TimeSpan(11, 0, 0), close: new TimeSpan(23, 0, 0));

        var tonight = await BookAsync(tableId, BookedDay, "20:30:00", "22:30:00");
        tonight.Status.Should().Be(HttpStatusCode.OK, tonight.Body);

        var yesterday = await BookAsync(tableId, "2030-05-16", "20:30:00", "22:30:00");
        yesterday.Status.Should().Be(HttpStatusCode.BadRequest, yesterday.Body);
        yesterday.Body.Should().Contain("past dates");
    }

    private async Task<(HttpStatusCode Status, string Body)> BookAsync(
        Guid tableId, string date, string startTime, string endTime)
    {
        var response = await Client.PostAsJsonAsync("/api/reservations", new
        {
            tableId,
            customerName = "Grace Hopper",
            customerEmail = "grace@example.com",
            customerPhone = "+12125550143",
            reservationDate = $"{date}T00:00:00Z",
            startTime,
            endTime,
            numberOfGuests = 2,
        });

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<string>> GetSlotStartsAsync(string date)
    {
        var response = await Client.GetAsync($"/api/reservations/available-slots?date={date}&numberOfGuests=2");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var payload = JsonDocument.Parse(body);
        var root = payload.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue(body);

        return root.GetProperty("data").GetProperty("timeSlots")
            .EnumerateArray()
            .Select(slot => slot.GetProperty("startTime").GetString()!)
            .ToList();
    }

    /// <summary>Seeds Friday's service and the one active table every slot is offered on.</summary>
    private async Task<Guid> SeedFridayAsync(TimeSpan open, TimeSpan close)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.WorkingHours.RemoveRange(await context.WorkingHours.ToListAsync());
        context.Tables.RemoveRange(await context.Tables.ToListAsync());

        context.WorkingHours.Add(new WorkingHours
        {
            DayOfWeek = DayOfWeek.Friday,
            OpenTime = open,
            CloseTime = close,
            IsActive = true,
            IsClosed = false,
            CreatedBy = "test"
        });

        var table = new Table { TableNumber = "T-369", MaxGuests = 4, IsActive = true, CreatedBy = "test" };
        context.Tables.Add(table);

        await context.SaveChangesAsync();

        return table.Id;
    }

    /// <summary>
    /// A clock stopped at an instant the test picks, in a zone the test picks. Hand-written for the
    /// same reason <c>WorkingHoursClockTests</c> writes its own: the point of
    /// <see cref="ITenantClock"/> being an interface is that a test can hold one.
    /// </summary>
    private sealed class MutableClock : ITenantClock
    {
        private DateTimeOffset _instant;

        public MutableClock(DateTimeOffset instant, string zoneId)
        {
            _instant = instant;
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }

        public TimeZoneInfo TimeZone { get; private set; }

        public DateTimeOffset Now => ToTenantTime(_instant.UtcDateTime);

        public void Set(DateTimeOffset instant, string zoneId)
        {
            _instant = instant;
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }

        public DateTimeOffset ToTenantTime(DateTime value) =>
            TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)), TimeZone);
    }
}
