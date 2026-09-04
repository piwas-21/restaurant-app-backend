using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

/// <summary>
/// The bundle schedule predicate itself (backend #397), called directly. Pure unit tests: the
/// predicate takes a day and a time of day as arguments, so nothing here needs a host or a database.
/// <para>
/// Called directly ON PURPOSE. A window that crosses midnight and a summer offset are both cases the
/// database cannot be asked to produce — the seeded rows decide neither the clock nor the wall time —
/// so the only way to make the fixture hostile is to hand the predicate the hostile values itself.
/// The HTTP-level agreement between the list and the by-id read is pinned separately, in
/// <c>MenuBundleScheduleTests</c>.
/// </para>
/// </summary>
public class MenuScheduleWindowTests
{
    private static readonly TimeSpan TenPm = new(22, 0, 0);
    private static readonly TimeSpan TwoAm = new(2, 0, 0);
    private static readonly TimeSpan Eleven = new(11, 0, 0);
    private static readonly TimeSpan TwoPm = new(14, 0, 0);

    /// <summary>
    /// The defect the wrap fix is about: <c>&gt;= Start &amp;&amp; &lt;= End</c> on a 22:00-02:00
    /// window is <c>time &gt;= 22:00 &amp;&amp; time &lt;= 02:00</c>, which no time of day satisfies.
    /// A late-night menu was therefore orderable at no moment at all.
    /// </summary>
    [Theory]
    [InlineData(23, 0, true)]   // Friday night, inside the evening half
    [InlineData(22, 0, true)]   // the opening edge, inclusive as before
    [InlineData(21, 59, false)] // one minute early
    public void A_window_that_crosses_midnight_is_open_on_its_evening_half(int hour, int minute, bool expected)
    {
        var bundle = Bundle(TenPm, TwoAm, DayOfWeek.Friday);

        IsOpen(bundle, DayOfWeek.Friday, new TimeSpan(hour, minute, 0)).Should().Be(expected);
    }

    /// <summary>
    /// 01:00 on Saturday belongs to FRIDAY's window — the window is named by the day it opened on,
    /// which is how a human reads "Friday, 22:00-02:00". Saturday's own flag is off here, so a
    /// predicate that read the small hours against today's flag answers false.
    /// </summary>
    [Fact]
    public void The_small_hours_belong_to_the_day_the_window_opened_on()
    {
        var bundle = Bundle(TenPm, TwoAm, DayOfWeek.Friday);

        IsOpen(bundle, DayOfWeek.Saturday, new TimeSpan(1, 0, 0)).Should().BeTrue();
        IsOpen(bundle, DayOfWeek.Saturday, TwoAm).Should().BeTrue("the closing edge is inclusive");
        IsOpen(bundle, DayOfWeek.Saturday, new TimeSpan(2, 1, 0)).Should().BeFalse();
        IsOpen(bundle, DayOfWeek.Saturday, new TimeSpan(23, 0, 0)).Should()
            .BeFalse("Saturday is not one of the days this bundle is served on");
    }

    [Fact]
    public void An_ordinary_window_is_open_between_its_ends_and_shut_outside_them()
    {
        var bundle = Bundle(Eleven, TwoPm, DayOfWeek.Friday);

        IsOpen(bundle, DayOfWeek.Friday, new TimeSpan(13, 0, 0)).Should().BeTrue();
        IsOpen(bundle, DayOfWeek.Friday, Eleven).Should().BeTrue();
        IsOpen(bundle, DayOfWeek.Friday, TwoPm).Should().BeTrue();
        IsOpen(bundle, DayOfWeek.Friday, new TimeSpan(16, 0, 0)).Should().BeFalse();
        IsOpen(bundle, DayOfWeek.Saturday, new TimeSpan(13, 0, 0)).Should()
            .BeFalse("the day flag decides before the time does");
    }

    [Fact]
    public void An_always_available_bundle_ignores_both_the_day_and_the_window()
    {
        var bundle = Bundle(Eleven, TwoPm, DayOfWeek.Friday);
        bundle.MenuDefinition!.IsAlwaysAvailable = true;

        IsOpen(bundle, DayOfWeek.Sunday, new TimeSpan(4, 0, 0)).Should().BeTrue();
    }

    [Fact]
    public void A_bundle_with_no_window_is_decided_by_its_day_flags_alone()
    {
        var bundle = Bundle(start: null, end: null, DayOfWeek.Friday);

        IsOpen(bundle, DayOfWeek.Friday, new TimeSpan(4, 0, 0)).Should().BeTrue();
        IsOpen(bundle, DayOfWeek.Saturday, new TimeSpan(13, 0, 0)).Should().BeFalse();
    }

    /// <summary>
    /// A half-written window matches nothing — the behaviour this replaced, kept deliberately. One
    /// end without the other is a data fault, and reading it as "unrestricted" would publish a
    /// bundle nobody asked to publish.
    /// </summary>
    [Fact]
    public void A_window_with_only_one_end_set_is_shut()
    {
        IsOpen(Bundle(Eleven, end: null, DayOfWeek.Friday), DayOfWeek.Friday, new TimeSpan(13, 0, 0))
            .Should().BeFalse();
        IsOpen(Bundle(start: null, TwoPm, DayOfWeek.Friday), DayOfWeek.Friday, new TimeSpan(13, 0, 0))
            .Should().BeFalse();
    }

    /// <summary>
    /// The clock half of #397, with the REAL <see cref="TenantClock"/> rather than a hand-picked
    /// offset: one UTC instant, two dates, and Paris is +01:00 on one and +02:00 on the other. A
    /// filter reading <c>DateTime.UtcNow</c> sees 12:30 both times and calls both of them lunch;
    /// on the wall clock only the winter one is.
    /// </summary>
    [Theory]
    [InlineData("2026-01-15T12:30:00Z", true)]   // 13:30 in Paris — inside 11:00-14:00
    [InlineData("2026-07-15T12:30:00Z", false)]  // 14:30 in Paris — lunch is over
    public void The_window_is_read_on_the_tenant_wall_clock_not_UTC(string instant, bool expected)
    {
        var bundle = Bundle(Eleven, TwoPm);
        bundle.MenuDefinition!.AvailableMonday = true;
        bundle.MenuDefinition.AvailableTuesday = true;
        bundle.MenuDefinition.AvailableWednesday = true;
        bundle.MenuDefinition.AvailableThursday = true;
        bundle.MenuDefinition.AvailableFriday = true;
        bundle.MenuDefinition.AvailableSaturday = true;
        bundle.MenuDefinition.AvailableSunday = true;

        var now = ParisClock().ToTenantTime(DateTime.Parse(
            instant, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal));

        IsOpen(bundle, now.DayOfWeek, now.TimeOfDay).Should().Be(expected);
    }

    private static bool IsOpen(Product bundle, DayOfWeek day, TimeSpan timeOfDay)
    {
        Expression<Func<Product, bool>> predicate = MenuScheduleWindow.AvailableAt(day, timeOfDay);
        return predicate.Compile()(bundle);
    }

    /// <param name="openOn">The single day the bundle is served on, or null for none.</param>
    private static Product Bundle(TimeSpan? start, TimeSpan? end, DayOfWeek? openOn = null) => new()
    {
        Name = "#397 bundle",
        CreatedBy = "test",
        MenuDefinition = new MenuDefinition
        {
            CreatedBy = "test",
            IsAlwaysAvailable = false,
            StartTime = start,
            EndTime = end,
            AvailableMonday = openOn == DayOfWeek.Monday,
            AvailableTuesday = openOn == DayOfWeek.Tuesday,
            AvailableWednesday = openOn == DayOfWeek.Wednesday,
            AvailableThursday = openOn == DayOfWeek.Thursday,
            AvailableFriday = openOn == DayOfWeek.Friday,
            AvailableSaturday = openOn == DayOfWeek.Saturday,
            AvailableSunday = openOn == DayOfWeek.Sunday
        }
    };

    private static TenantClock ParisClock() =>
        new(Options.Create(new LocalizationSettings { TimeZone = "Europe/Paris" }),
            NullLogger<TenantClock>.Instance);
}
