using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// The tenant's CALENDAR DAY, expressed as the pair of UTC instants a query can filter on.
/// </summary>
/// <remarks>
/// <para>
/// An extension rather than a member of <see cref="ITenantClock"/> on purpose: the zone still comes
/// from the clock — there is one time source (backend #363) — but every test double of that
/// interface inherits this conversion instead of re-deriving it, which is what a day-boundary rule
/// duplicated per fake would get wrong quietly (backend #372).
/// </para>
/// <para>
/// A local day is NOT always 24 hours. In <c>Europe/Zurich</c> the last Sunday of March is 23 h and
/// the last Sunday of October is 25 h, so the window is built from two independent local midnights
/// rather than from one plus <c>AddDays(1)</c> on the instant.
/// </para>
/// </remarks>
public static class TenantClockExtensions
{
    /// <summary>
    /// The half-open UTC window <c>[start, end)</c> covering the tenant's local calendar
    /// <paramref name="date"/> from 00:00 to 24:00 on its own wall clock.
    /// </summary>
    /// <param name="clock">The tenant clock whose <see cref="ITenantClock.TimeZone"/> defines the day.</param>
    /// <param name="date">The calendar day, as the operator names it — no time, no zone.</param>
    public static (DateTime StartUtc, DateTime EndUtc) TenantDayWindowUtc(this ITenantClock clock, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return (StartOfDayUtc(clock.TimeZone, date), StartOfDayUtc(clock.TimeZone, date.AddDays(1)));
    }

    /// <summary>The first instant of a local calendar day, in UTC.</summary>
    private static DateTime StartOfDayUtc(TimeZoneInfo zone, DateOnly day)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return new DateTimeOffset(localMidnight, OffsetAtLocalMidnight(zone, localMidnight)).UtcDateTime;
    }

    /// <summary>
    /// The offset in force at a local midnight, resolving the two cases a DST transition AT
    /// midnight creates. Both are real: <c>America/Santiago</c> springs forward at 00:00 and
    /// <c>America/Havana</c> falls back at 00:00 (and <c>Europe/Zurich</c> does neither, which is
    /// why this cannot be checked with the tenant's own zone alone).
    /// </summary>
    private static TimeSpan OffsetAtLocalMidnight(TimeZoneInfo zone, DateTime localMidnight)
    {
        // Fall back: midnight happens TWICE. The day starts at the first of them, which is the
        // LARGER offset (utc = local - offset). TimeZoneInfo.GetUtcOffset would answer with the
        // standard one and silently drop the day's first hour of trading.
        if (zone.IsAmbiguousTime(localMidnight))
        {
            return zone.GetAmbiguousTimeOffsets(localMidnight).Max();
        }

        // Spring forward: midnight never happened. The day starts at the instant the clock jumped,
        // which is local midnight read on the offset in force just BEFORE the gap. GetUtcOffset
        // answers an invalid time with the zone's STANDARD offset instead, and that is not always
        // the pre-gap one: scanning every zone this host knows over 1990-2035, it lands the start an
        // hour late on 43 zone-days (America/Scoresbysund every March to 2022, six Argentina zones
        // in 1991-92) and never earlier — an hour of trading dropped from the day's takings.
        if (zone.IsInvalidTime(localMidnight))
        {
            return zone.GetUtcOffset(localMidnight.AddDays(-1));
        }

        return zone.GetUtcOffset(localMidnight);
    }
}
