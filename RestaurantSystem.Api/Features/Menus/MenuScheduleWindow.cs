using System.Linq.Expressions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// Whether a bundle's <see cref="MenuDefinition"/> schedule is open at a given moment — ONE
/// definition, shared by the list and the by-id read, because those two disagreeing is the defect
/// this type was extracted for (backend #397: the list hid a bundle the detail endpoint then served).
/// </summary>
/// <remarks>
/// The caller supplies the day and the time of day <b>on the tenant's wall clock</b>
/// (<c>ITenantClock.Now</c>) — never <c>DateTime.UtcNow</c>, which is what this replaced and is two
/// hours off in a Zurich or Paris summer, so an 11:00–14:00 lunch menu appeared at 13:00 and vanished
/// at 16:00 local.
/// <para>
/// It returns an <see cref="Expression{TDelegate}"/> rather than a <c>bool</c> so the SAME predicate
/// is the one PostgreSQL evaluates for the list and for the detail read; a second, hand-evaluated
/// copy for the by-id path is exactly how the two drifted apart in the first place. A test can still
/// call it directly — <c>Compile()</c> it and feed it a hostile <see cref="Product"/> — which is the
/// only way to pin the midnight-wrap and DST cases without asking the database to produce a clock.
/// </para>
/// <para>
/// Deliberately NOT reused for working hours or order types. <c>WorkingHoursWindows</c> answers a
/// different question over a different table (and carries its own, per-day shift rows); copying this
/// filter into a neighbouring feature is what #397 warns against.
/// </para>
/// </remarks>
public static class MenuScheduleWindow
{
    /// <summary>
    /// Bundles whose schedule is open at <paramref name="timeOfDay"/> on <paramref name="day"/>.
    /// </summary>
    /// <param name="day">The day on the tenant's wall clock.</param>
    /// <param name="timeOfDay">The time on the tenant's wall clock.</param>
    /// <remarks>
    /// A window with only one end set (start without end, or the reverse) matches nothing, which is
    /// the behaviour this replaced — a half-written window is a data fault, and widening it here
    /// would silently publish bundles no one asked to publish. The bundle editor writes both ends
    /// or neither.
    /// </remarks>
    public static Expression<Func<Product, bool>> AvailableAt(DayOfWeek day, TimeSpan timeOfDay)
    {
        // A wrapping window (22:00–02:00) belongs to the day it OPENED on, so 01:00 tonight is
        // inside YESTERDAY's window and yesterday's day flag is the one that decides it.
        var previousDay = (DayOfWeek)(((int)day + 6) % 7);

        return p =>
            p.MenuDefinition!.IsAlwaysAvailable
            || (
                (
                    (day == DayOfWeek.Monday && p.MenuDefinition.AvailableMonday)
                    || (day == DayOfWeek.Tuesday && p.MenuDefinition.AvailableTuesday)
                    || (day == DayOfWeek.Wednesday && p.MenuDefinition.AvailableWednesday)
                    || (day == DayOfWeek.Thursday && p.MenuDefinition.AvailableThursday)
                    || (day == DayOfWeek.Friday && p.MenuDefinition.AvailableFriday)
                    || (day == DayOfWeek.Saturday && p.MenuDefinition.AvailableSaturday)
                    || (day == DayOfWeek.Sunday && p.MenuDefinition.AvailableSunday)
                )
                && (
                    // No window at all — the day flags alone decide.
                    (p.MenuDefinition.StartTime == null && p.MenuDefinition.EndTime == null)
                    // An ordinary window, both ends on the same day. Inclusive at both ends, as
                    // before. Either end being null makes every comparison false, so a half-written
                    // window falls through to "closed" without a null check of its own.
                    || (p.MenuDefinition.StartTime <= p.MenuDefinition.EndTime
                        && timeOfDay >= p.MenuDefinition.StartTime
                        && timeOfDay <= p.MenuDefinition.EndTime)
                    // The evening half of a wrapping window.
                    || (p.MenuDefinition.StartTime > p.MenuDefinition.EndTime
                        && timeOfDay >= p.MenuDefinition.StartTime)
                )
            )
            // The small-hours half of a wrapping window, carried over from the previous day.
            || (
                (
                    (previousDay == DayOfWeek.Monday && p.MenuDefinition.AvailableMonday)
                    || (previousDay == DayOfWeek.Tuesday && p.MenuDefinition.AvailableTuesday)
                    || (previousDay == DayOfWeek.Wednesday && p.MenuDefinition.AvailableWednesday)
                    || (previousDay == DayOfWeek.Thursday && p.MenuDefinition.AvailableThursday)
                    || (previousDay == DayOfWeek.Friday && p.MenuDefinition.AvailableFriday)
                    || (previousDay == DayOfWeek.Saturday && p.MenuDefinition.AvailableSaturday)
                    || (previousDay == DayOfWeek.Sunday && p.MenuDefinition.AvailableSunday)
                )
                && p.MenuDefinition.StartTime > p.MenuDefinition.EndTime
                && timeOfDay <= p.MenuDefinition.EndTime
            );
    }
}
