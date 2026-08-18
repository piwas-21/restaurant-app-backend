using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// The tenant's calendar day as the pair of instants a query filters on (backend #372). Pure unit
/// tests: the zone is configuration and the day is an argument, so nothing here needs a host.
/// <para>
/// The real <see cref="TenantClock"/> is used rather than a double, because the zone lookup is half
/// of what is under test — a fake carrying a hand-picked <see cref="TimeSpan"/> would agree with any
/// implementation, including one that never asks the zone about DST at all.
/// </para>
/// </summary>
public class TenantDayWindowTests
{
    /// <summary>
    /// A plain day: Zurich is on +02:00 in May, so the till's day runs 22:00Z to 22:00Z — NOT the
    /// 00:00Z-to-00:00Z window the report used to cover.
    /// </summary>
    [Fact]
    public void An_ordinary_day_is_the_local_midnights_not_the_UTC_ones()
    {
        var (start, end) = Clock().TenantDayWindowUtc(new DateOnly(2026, 5, 2));

        start.Should().Be(new DateTime(2026, 5, 1, 22, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 5, 2, 22, 0, 0, DateTimeKind.Utc));
        start.Kind.Should().Be(DateTimeKind.Utc);
        end.Kind.Should().Be(DateTimeKind.Utc);
    }

    /// <summary>
    /// The reason the window is built from two independent local midnights: on 29 March 2026 Zurich
    /// loses an hour, so <c>start.AddDays(1)</c> would run the till an hour into the 30th and count
    /// the next day's first orders twice — once in each report.
    /// </summary>
    [Fact]
    public void The_spring_forward_day_is_twenty_three_hours_long()
    {
        var (start, end) = Clock().TenantDayWindowUtc(new DateOnly(2026, 3, 29));

        start.Should().Be(new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc));
        (end - start).Should().Be(TimeSpan.FromHours(23));
    }

    /// <summary>The other direction: 25 October 2026 in Zurich is 25 hours, and all of them sell.</summary>
    [Fact]
    public void The_fall_back_day_is_twenty_five_hours_long()
    {
        var (start, end) = Clock().TenantDayWindowUtc(new DateOnly(2026, 10, 25));

        start.Should().Be(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc));
        (end - start).Should().Be(TimeSpan.FromHours(25));
    }

    /// <summary>
    /// Santiago's clock jumps from 23:59:59 straight to 01:00 on 6 September 2026: local midnight
    /// never happens. The day has to start at the instant of the jump (04:00Z, the -04:00 reading of
    /// a midnight that was skipped), because that is when the wall clock entered the 6th.
    /// <c>TimeZoneInfo.GetUtcOffset</c> answers such a time with the offset AFTER the gap (-03:00),
    /// which would put the start an hour late and drop whatever was sold up to the jump.
    /// </summary>
    [Fact]
    public void A_midnight_that_never_happened_starts_the_day_at_the_jump()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
        var day = new DateOnly(2026, 9, 6);

        // The premise, asserted rather than assumed: if tzdata ever moves this transition, the
        // assertions below stop testing the gap branch and this says so.
        zone.IsInvalidTime(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified))
            .Should().BeTrue("00:00 on 2026-09-06 is skipped in Santiago");

        var (start, end) = Clock("America/Santiago").TenantDayWindowUtc(day);

        start.Should().Be(new DateTime(2026, 9, 6, 4, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc));
        (end - start).Should().Be(TimeSpan.FromHours(23));
    }

    /// <summary>
    /// Havana falls back at 01:00 DST on 1 November 2026, so midnight happens TWICE. The day starts
    /// at the FIRST one (04:00Z, the -04:00 reading); <c>GetUtcOffset</c> would answer with the
    /// standard -05:00 and quietly drop the first hour of trading from the day's takings.
    /// </summary>
    [Fact]
    public void A_midnight_that_happened_twice_starts_the_day_at_the_first_one()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");
        var day = new DateOnly(2026, 11, 1);

        zone.IsAmbiguousTime(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified))
            .Should().BeTrue("00:00 on 2026-11-01 happens twice in Havana");

        var (start, end) = Clock("America/Havana").TenantDayWindowUtc(day);

        start.Should().Be(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc));
        (end - start).Should().Be(TimeSpan.FromHours(25));
    }

    /// <summary>
    /// The gap case that <c>GetUtcOffset</c> alone gets WRONG, and the reason the invalid-time
    /// branch exists at all. Scoresbysund's clock jumped from 00:00 to 01:00 on 27 March 2022:
    /// .NET answers an invalid local time with the STANDARD offset (-02:00 here — the zone was on
    /// -01:00 standard until the jump changed BOTH sides), which places the day's start at 02:00Z,
    /// an hour after the wall clock actually entered the 27th.
    /// <para>
    /// Measured rather than argued: scanning every zone this host knows over 1990-2035 for a local
    /// midnight that does not exist, the standard-offset reading is an hour late on 43 zone-days
    /// (Scoresbysund every March 1990-2022, and six Argentina zones in 1991/1992) and the pre-gap
    /// reading matches the true first instant on ALL of them. Santiago above is a case where the
    /// two agree, which is why it cannot pin this branch on its own.
    /// </para>
    /// </summary>
    [Fact]
    public void A_gap_whose_standard_offset_is_not_the_pre_gap_offset_still_starts_at_the_jump()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Scoresbysund");
        var day = new DateOnly(2022, 3, 27);
        var localMidnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        zone.IsInvalidTime(localMidnight).Should().BeTrue("00:00 on 2022-03-27 is skipped there");
        zone.GetUtcOffset(localMidnight).Should().Be(
            TimeSpan.FromHours(-2), "the standard offset is NOT the one in force before the gap");

        var (start, end) = Clock("America/Scoresbysund").TenantDayWindowUtc(day);

        start.Should().Be(new DateTime(2022, 3, 27, 1, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2022, 3, 28, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>Consecutive days meet exactly: no gap the till could lose an order in, no overlap.</summary>
    [Fact]
    public void Consecutive_days_abut_across_a_transition()
    {
        var clock = Clock();

        var (_, endOf28th) = clock.TenantDayWindowUtc(new DateOnly(2026, 3, 28));
        var (startOf29th, _) = clock.TenantDayWindowUtc(new DateOnly(2026, 3, 29));

        startOf29th.Should().Be(endOf28th);
    }

    private static TenantClock Clock(string? timeZone = null) =>
        new(
            Options.Create(new LocalizationSettings { TimeZone = timeZone ?? string.Empty }),
            NullLogger<TenantClock>.Instance);
}
