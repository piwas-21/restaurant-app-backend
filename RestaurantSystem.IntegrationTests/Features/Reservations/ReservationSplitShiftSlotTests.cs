using System.Net;
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
/// The reservation slot generator reads the same opening hours as the "are we open" banner, and
/// before G11 it read them the same WRONG way: one interval from the day's opening to its closing.
/// On a restaurant that serves 11:00-15:00 and 18:00-23:00 that offered a guest a table at 16:00,
/// in a dining room with the lights off — the same falsehood as the open sign, addressed to someone
/// who is about to drive there.
/// </summary>
[Collection("Database Lane 2")]
public class ReservationSplitShiftSlotTests : IntegrationTestBase
{
    /// <summary>Friday 2030-05-17 — far enough ahead that no slot is filtered as past.</summary>
    private const string BookedDay = "2030-05-17";

    private readonly MutableClock _clock = new(
        new DateTimeOffset(2030, 5, 10, 9, 0, 0, TimeSpan.Zero), "Europe/Zurich");

    public ReservationSplitShiftSlotTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantClock>(_clock);
    }

    [Fact]
    public async Task No_table_is_offered_inside_the_closure()
    {
        await SeedSplitShiftFridayAsync();

        var slots = await GetSlotStartsAsync(BookedDay);

        // Written as the WHOLE expected list, not as a "does not contain 16:00" filter: the exact
        // enumeration is what pins the two window ENDS as well as the gap. 2h slots on 30-minute
        // starts, so lunch stops offering at 13:00 (13:00 + 2h = 15:00) and dinner at 21:00.
        slots.Should().Equal(
            "11:00:00", "11:30:00", "12:00:00", "12:30:00", "13:00:00",
            "18:00:00", "18:30:00", "19:00:00", "19:30:00", "20:00:00", "20:30:00", "21:00:00");
    }

    [Fact]
    public async Task A_single_window_day_is_unchanged()
    {
        // The regression control. Every tenant on the platform today is this shape, and the fix
        // must not move a single one of their slots.
        await SeedFridayAsync(day =>
        {
            day.OpenTime = new TimeSpan(11, 0, 0);
            day.CloseTime = new TimeSpan(15, 0, 0);
            day.Shifts.Add(new WorkingHoursShift
            {
                OpenTime = new TimeSpan(11, 0, 0),
                CloseTime = new TimeSpan(15, 0, 0),
                CreatedBy = "test"
            });
        });

        var slots = await GetSlotStartsAsync(BookedDay);

        slots.Should().Equal("11:00:00", "11:30:00", "12:00:00", "12:30:00", "13:00:00");
    }

    private async Task<List<string>> GetSlotStartsAsync(string date)
    {
        var response = await Client.GetAsync(
            $"/api/reservations/available-slots?date={date}&numberOfGuests=2");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var payload = JsonDocument.Parse(body);

        // The endpoint answers a refused query with HTTP 200 and success:false, so the status code
        // alone would pass on an error.
        payload.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(body);

        return payload.RootElement.GetProperty("data").GetProperty("timeSlots")
            .EnumerateArray()
            .Select(slot => slot.GetProperty("startTime").GetString()!)
            .ToList();
    }

    private Task SeedSplitShiftFridayAsync() =>
        SeedFridayAsync(day =>
        {
            day.OpenTime = new TimeSpan(11, 0, 0);
            day.CloseTime = new TimeSpan(15, 0, 0);
            day.Shifts.Add(new WorkingHoursShift
            {
                OpenTime = new TimeSpan(11, 0, 0),
                CloseTime = new TimeSpan(15, 0, 0),
                CreatedBy = "test"
            });
            day.Shifts.Add(new WorkingHoursShift
            {
                OpenTime = new TimeSpan(18, 0, 0),
                CloseTime = new TimeSpan(23, 0, 0),
                CreatedBy = "test"
            });
        });

    private async Task SeedFridayAsync(Action<WorkingHours> configure)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.WorkingHours.RemoveRange(await context.WorkingHours.ToListAsync());
        context.Tables.RemoveRange(await context.Tables.ToListAsync());

        var day = new WorkingHours
        {
            DayOfWeek = DayOfWeek.Friday,
            IsActive = true,
            IsClosed = false,
            CreatedBy = "test"
        };

        configure(day);
        context.WorkingHours.Add(day);

        context.Tables.Add(new Table
        {
            TableNumber = "T-G11",
            MaxGuests = 4,
            IsActive = true,
            CreatedBy = "test"
        });

        await context.SaveChangesAsync();
    }
}
