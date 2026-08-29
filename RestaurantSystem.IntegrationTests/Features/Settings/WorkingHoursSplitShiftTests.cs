using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Settings;

/// <summary>
/// A day is N serving windows, not one (G11). Kebab d'Ilhan trades 11:00-15:00 AND 18:00-23:00
/// seven days a week; on the single-interval model the only storable answer was 11:00-23:00, which
/// tells a customer the shop is serving at 16:00 when it is dark and the door is locked.
/// <para>
/// The load-bearing test here is <see cref="Sixteen_hundred_on_a_split_shift_day_is_CLOSED"/>. The
/// evening assertions are load-bearing for a different reason: the window reader falls back to the
/// legacy <c>OpenTime</c>/<c>CloseTime</c> pair when a day has no shift rows, and an un-Included
/// collection is empty, so a forgotten <c>.Include(wh =&gt; wh.Shifts)</c> would answer from the
/// LUNCH window alone and report this restaurant as shut every evening. A test that only asked
/// about lunch would pass with the Include deleted.
/// </para>
/// </summary>
[Collection("Database Lane 1")]
public class WorkingHoursSplitShiftTests : IntegrationTestBase
{
    /// <summary>Friday 2030-05-17. Zurich is on CEST that day, so local = UTC + 2.</summary>
    private const int ZurichSummerOffsetHours = 2;

    private static readonly TimeSpan LunchOpen = new(11, 0, 0);
    private static readonly TimeSpan LunchClose = new(15, 0, 0);
    private static readonly TimeSpan DinnerOpen = new(18, 0, 0);
    private static readonly TimeSpan DinnerClose = new(23, 0, 0);

    private readonly MutableClock _clock = new(
        new DateTimeOffset(2030, 5, 17, 10, 0, 0, TimeSpan.Zero), "Europe/Zurich");

    public WorkingHoursSplitShiftTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantClock>(_clock);
    }

    // ── The point of the change ──────────────────────────────────────────

    [Fact]
    public async Task Sixteen_hundred_on_a_split_shift_day_is_CLOSED()
    {
        await SeedSplitShiftFridayAsync();
        AtLocalTime(16, 0);

        (await IsOpenNowAsync()).Should().BeFalse(
            "16:00 falls in the gap between lunch (11:00-15:00) and dinner (18:00-23:00)");
    }

    [Theory]
    [InlineData(9, 0, false)]   // before lunch
    [InlineData(11, 0, true)]   // lunch opens
    [InlineData(13, 30, true)]  // mid lunch
    [InlineData(15, 30, false)] // the closure
    [InlineData(17, 59, false)] // one minute before dinner
    [InlineData(18, 0, true)]   // dinner opens
    [InlineData(20, 0, true)]   // mid dinner — also catches a dropped .Include (see class remarks)
    [InlineData(23, 0, true)]   // the closing instant is inclusive, as it was on one window
    [InlineData(23, 30, false)] // after service
    public async Task Each_hour_answers_from_the_window_it_falls_in(int hour, int minute, bool expected)
    {
        await SeedSplitShiftFridayAsync();
        AtLocalTime(hour, minute);

        (await IsOpenNowAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task A_day_with_no_shift_rows_still_answers_from_the_legacy_pair()
    {
        // The shape every tenant is on before this migration runs, and the shape a row written by
        // an older seeder keeps. It has to keep working: a platform that answered "closed" for an
        // un-backfilled day would shut every restaurant on the box for the length of a deploy.
        await SeedLegacyFridayAsync(new TimeSpan(10, 0, 0), new TimeSpan(23, 0, 0));

        AtLocalTime(16, 0);
        (await IsOpenNowAsync()).Should().BeTrue("a single-window day really is open at 16:00");

        AtLocalTime(23, 30);
        (await IsOpenNowAsync()).Should().BeFalse();
    }

    // ── The write path ───────────────────────────────────────────────────

    [Fact]
    public async Task The_legacy_pair_mirrors_the_FIRST_window_by_opening_time()
    {
        await SeedLegacyFridayAsync(new TimeSpan(10, 0, 0), new TimeSpan(23, 0, 0));
        AuthenticateAsAdmin();

        // DINNER FIRST in the posted array, deliberately. The mirror is defined as the earliest
        // window, not the first one typed, and an implementation that took `Shifts[0]` would
        // publish 18:00 as the day's opening time to every client that has not learned about
        // shifts — a customer would be told the kitchen opens at six.
        var day = await PutDayAsync(new object[]
        {
            new { openTime = "18:00:00", closeTime = "23:00:00" },
            new { openTime = "11:00:00", closeTime = "15:00:00" },
        });

        day.GetProperty("openTime").GetString().Should().Be("11:00:00");
        day.GetProperty("closeTime").GetString().Should().Be("15:00:00");

        ShiftsOf(day).Should().Equal(("11:00:00", "15:00:00"), ("18:00:00", "23:00:00"));
    }

    [Fact]
    public async Task A_body_with_no_shifts_field_is_stored_as_one_window()
    {
        // The mobile client's body, and the body this endpoint took before shifts existed. `null`
        // means "I do not know about this field", so the pair IS the day — anything else would
        // silently blank a restaurant's hours the first time an un-updated client saved.
        await SeedLegacyFridayAsync(new TimeSpan(10, 0, 0), new TimeSpan(23, 0, 0));
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync("/api/WorkingHours", new
        {
            dayOfWeek = (int)DayOfWeek.Friday,
            openTime = "12:00:00",
            closeTime = "22:00:00",
            isActive = true,
            isClosed = false,
        });

        var day = await ReadDayAsync(response);

        ShiftsOf(day).Should().Equal(("12:00:00", "22:00:00"));
        day.GetProperty("openTime").GetString().Should().Be("12:00:00");
    }

    [Fact]
    public async Task An_open_day_sent_an_EMPTY_window_list_is_refused()
    {
        // `[]` is not `null`. A caller that knows about the field and sent nothing has made a
        // mistake; treating it as "closed all day" would take a restaurant off the internet on a
        // malformed save, and "the field was omitted" and "we serve nobody" must not be the same
        // payload (the S6964 under-posting trap, in list form).
        await SeedLegacyFridayAsync(new TimeSpan(10, 0, 0), new TimeSpan(23, 0, 0));
        AuthenticateAsAdmin();

        var (status, body) = await PutRawAsync(Array.Empty<object>(), isClosed: false);

        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("at least one opening window");
    }

    [Fact]
    public async Task Overlapping_windows_are_refused()
    {
        await SeedLegacyFridayAsync(new TimeSpan(10, 0, 0), new TimeSpan(23, 0, 0));
        AuthenticateAsAdmin();

        var (status, body) = await PutRawAsync(
            new object[]
            {
                new { openTime = "11:00:00", closeTime = "16:00:00" },
                new { openTime = "15:00:00", closeTime = "23:00:00" },
            },
            isClosed: false);

        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("overlap");
    }

    [Fact]
    public async Task Windows_that_merely_touch_are_allowed()
    {
        // The negative control for the overlap rule: 15:00-15:00 is a handover, not an overlap,
        // and a rule written with `<=` instead of `<` would refuse a legal split.
        await SeedLegacyFridayAsync(new TimeSpan(10, 0, 0), new TimeSpan(23, 0, 0));
        AuthenticateAsAdmin();

        var day = await PutDayAsync(new object[]
        {
            new { openTime = "11:00:00", closeTime = "15:00:00" },
            new { openTime = "15:00:00", closeTime = "23:00:00" },
        });

        ShiftsOf(day).Should().Equal(("11:00:00", "15:00:00"), ("15:00:00", "23:00:00"));
    }

    [Fact]
    public async Task A_window_that_closes_before_it_opens_is_refused()
    {
        await SeedLegacyFridayAsync(new TimeSpan(10, 0, 0), new TimeSpan(23, 0, 0));
        AuthenticateAsAdmin();

        var (status, body) = await PutRawAsync(
            new object[] { new { openTime = "18:00:00", closeTime = "02:00:00" } },
            isClosed: false);

        // Overnight service is NOT supported and this is where it is refused rather than stored as
        // a window that can never contain any instant. Making 18:00-02:00 mean "until 2am tomorrow"
        // is a separate decision about which calendar day 01:00 belongs to.
        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("Closing time must be after opening time");
    }

    [Fact]
    public async Task A_closed_day_keeps_its_windows_so_the_toggle_is_reversible()
    {
        // An admin who shuts Monday and saves must find Monday's hours still there when they open
        // it again. The windows are stored and simply never read while the day is closed.
        await SeedSplitShiftFridayAsync();
        AuthenticateAsAdmin();

        var closed = await PutRawAsync(
            new object[]
            {
                new { openTime = "11:00:00", closeTime = "15:00:00" },
                new { openTime = "18:00:00", closeTime = "23:00:00" },
            },
            isClosed: true);

        closed.Status.Should().Be(HttpStatusCode.OK, closed.Body);

        AtLocalTime(13, 0);
        (await IsOpenNowAsync()).Should().BeFalse("the day is marked closed");

        using var payload = JsonDocument.Parse(closed.Body);
        ShiftsOf(payload.RootElement.GetProperty("data"))
            .Should().Equal(("11:00:00", "15:00:00"), ("18:00:00", "23:00:00"));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the clock to <paramref name="hour"/>:<paramref name="minute"/> as the RESTAURANT reads
    /// it, and asserts that it landed there — the offset arithmetic is the test's own, so an error
    /// in it would otherwise silently retarget every assertion above.
    /// </summary>
    private void AtLocalTime(int hour, int minute)
    {
        _clock.Set(
            new DateTimeOffset(2030, 5, 17, hour - ZurichSummerOffsetHours, minute, 0, TimeSpan.Zero),
            "Europe/Zurich");

        _clock.Now.TimeOfDay.Should().Be(new TimeSpan(hour, minute, 0));
        _clock.Now.DayOfWeek.Should().Be(DayOfWeek.Friday);
    }

    private async Task<bool> IsOpenNowAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var hours = scope.ServiceProvider.GetRequiredService<IWorkingHoursService>();

        return await hours.IsOpenNowAsync();
    }

    private static List<(string Open, string Close)> ShiftsOf(JsonElement day) =>
        day.GetProperty("shifts").EnumerateArray()
            .Select(s => (s.GetProperty("openTime").GetString()!, s.GetProperty("closeTime").GetString()!))
            .ToList();

    private async Task<JsonElement> PutDayAsync(object[] shifts)
    {
        var (status, body) = await PutRawAsync(shifts, isClosed: false);
        status.Should().Be(HttpStatusCode.OK, body);

        using var payload = JsonDocument.Parse(body);
        return payload.RootElement.GetProperty("data").Clone();
    }

    private async Task<(HttpStatusCode Status, string Body)> PutRawAsync(object[] shifts, bool isClosed)
    {
        var response = await Client.PutAsJsonAsync("/api/WorkingHours", new
        {
            dayOfWeek = (int)DayOfWeek.Friday,
            openTime = "10:00:00",
            closeTime = "23:00:00",
            shifts,
            isActive = true,
            isClosed,
        });

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ReadDayAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var payload = JsonDocument.Parse(body);
        return payload.RootElement.GetProperty("data").Clone();
    }

    private Task SeedSplitShiftFridayAsync() =>
        SeedFridayAsync(day =>
        {
            day.OpenTime = LunchOpen;
            day.CloseTime = LunchClose;
            day.Shifts.Add(new WorkingHoursShift { OpenTime = LunchOpen, CloseTime = LunchClose, CreatedBy = "test" });
            day.Shifts.Add(new WorkingHoursShift { OpenTime = DinnerOpen, CloseTime = DinnerClose, CreatedBy = "test" });
        });

    /// <summary>A row in the pre-migration shape: the legacy pair and no shift rows at all.</summary>
    private Task SeedLegacyFridayAsync(TimeSpan open, TimeSpan close) =>
        SeedFridayAsync(day =>
        {
            day.OpenTime = open;
            day.CloseTime = close;
        });

    private async Task SeedFridayAsync(Action<WorkingHours> configure)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.WorkingHours.RemoveRange(await context.WorkingHours.ToListAsync());

        var day = new WorkingHours
        {
            DayOfWeek = DayOfWeek.Friday,
            IsActive = true,
            IsClosed = false,
            CreatedBy = "test"
        };

        configure(day);

        context.WorkingHours.Add(day);
        await context.SaveChangesAsync();
    }
}
