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
    /// Three ways in, and the third is the one that did not exist: a window that crosses midnight
    /// (22:00–02:00) belongs to the day it OPENED on, so 01:00 tonight is inside YESTERDAY's window
    /// and yesterday's day flag decides it.
    /// </remarks>
    public static Expression<Func<Product, bool>> AvailableAt(DayOfWeek day, TimeSpan timeOfDay)
    {
        var previousDay = (DayOfWeek)(((int)day + 6) % 7);

        return Or(
            AlwaysAvailable(),
            Or(
                And(ServedOn(day), WithinTodaysWindow(timeOfDay)),
                And(ServedOn(previousDay), WithinLastNightsWindow(timeOfDay))));
    }

    private static Expression<Func<Product, bool>> AlwaysAvailable() =>
        p => p.MenuDefinition!.IsAlwaysAvailable;

    /// <summary>The day flag for one day of the week — seven columns, one of which is asked.</summary>
    private static Expression<Func<Product, bool>> ServedOn(DayOfWeek day) =>
        p => (day == DayOfWeek.Monday && p.MenuDefinition!.AvailableMonday)
            || (day == DayOfWeek.Tuesday && p.MenuDefinition!.AvailableTuesday)
            || (day == DayOfWeek.Wednesday && p.MenuDefinition!.AvailableWednesday)
            || (day == DayOfWeek.Thursday && p.MenuDefinition!.AvailableThursday)
            || (day == DayOfWeek.Friday && p.MenuDefinition!.AvailableFriday)
            || (day == DayOfWeek.Saturday && p.MenuDefinition!.AvailableSaturday)
            || (day == DayOfWeek.Sunday && p.MenuDefinition!.AvailableSunday);

    /// <summary>
    /// The time test for a window that starts TODAY: no window at all, an ordinary window, or the
    /// evening half of one that crosses midnight.
    /// </summary>
    /// <remarks>
    /// A window with only one end set (start without end, or the reverse) matches nothing, which is
    /// the behaviour this replaced — a half-written window is a data fault, and widening it here
    /// would silently publish bundles no one asked to publish. Every comparison below is false when
    /// either end is null, in SQL and in a compiled delegate alike, so that case needs no branch of
    /// its own.
    /// </remarks>
    private static Expression<Func<Product, bool>> WithinTodaysWindow(TimeSpan timeOfDay) =>
        p => (p.MenuDefinition!.StartTime == null && p.MenuDefinition.EndTime == null)
            || (timeOfDay >= p.MenuDefinition.StartTime && timeOfDay <= p.MenuDefinition.EndTime)
            || (p.MenuDefinition.StartTime > p.MenuDefinition.EndTime
                && timeOfDay >= p.MenuDefinition.StartTime);

    /// <summary>
    /// The small-hours half of a window that opened the day before. <c>Start &gt; End</c> IS the
    /// wrap test, and it is false unless both ends are set.
    /// </summary>
    private static Expression<Func<Product, bool>> WithinLastNightsWindow(TimeSpan timeOfDay) =>
        p => p.MenuDefinition!.StartTime > p.MenuDefinition.EndTime
            && timeOfDay <= p.MenuDefinition.EndTime;

    private static Expression<Func<Product, bool>> And(
        Expression<Func<Product, bool>> left,
        Expression<Func<Product, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    private static Expression<Func<Product, bool>> Or(
        Expression<Func<Product, bool>> left,
        Expression<Func<Product, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    /// <summary>
    /// Joins two predicates over ONE parameter. Written as a tree rather than as one long C# lambda
    /// so each clause above stands alone and can be read — and so the day test is written once and
    /// asked twice (today, and the day a wrapping window opened on) instead of twice verbatim.
    /// <see cref="Expression.Invoke(Expression, Expression[])"/> is deliberately not used: EF Core
    /// does not translate an invocation, so the parameter is rebound instead and what reaches the
    /// provider is an ordinary boolean tree.
    /// </summary>
    private static Expression<Func<Product, bool>> Combine(
        Expression<Func<Product, bool>> left,
        Expression<Func<Product, bool>> right,
        Func<Expression, Expression, BinaryExpression> join)
    {
        var parameter = left.Parameters[0];
        var rebound = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body);

        return Expression.Lambda<Func<Product, bool>>(join(left.Body, rebound), parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
