namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Reads a day's serving windows. One place, because "is the shop open" and "may a guest book this
/// table" have to answer the same question the same way — they disagreed before, and the reservation
/// slot generator would have offered a 16:00 table on a day the site correctly showed as shut.
/// </summary>
public static class WorkingHoursWindows
{
    /// <summary>
    /// The day's windows in time order, correct for BOTH shapes: the shift rows when they exist,
    /// and the legacy <c>OpenTime</c>/<c>CloseTime</c> pair when they do not (a row seeded by an
    /// older path, or written directly by a test).
    /// <para>
    /// <b>The caller MUST have loaded the collection</b> — <c>.Include(wh =&gt; wh.Shifts)</c>. An
    /// un-included collection is empty, and an empty collection is indistinguishable here from a
    /// genuine single-shift day, so a forgotten Include does not throw: it quietly answers with the
    /// FIRST shift only and reports a split-shift restaurant as shut all evening. The integration
    /// tests assert an evening instant for exactly this reason.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(TimeSpan Open, TimeSpan Close)> Of(WorkingHours day)
    {
        ArgumentNullException.ThrowIfNull(day);

        if (day.Shifts.Count == 0)
        {
            return [(day.OpenTime, day.CloseTime)];
        }

        return day.Shifts
            .OrderBy(s => s.OpenTime)
            .Select(s => (s.OpenTime, s.CloseTime))
            .ToList();
    }

    /// <summary>
    /// Whether <paramref name="timeOfDay"/> falls inside any of the day's windows. Bounds are
    /// inclusive at both ends, which is what the single-window test it replaces did.
    /// </summary>
    public static bool Contains(WorkingHours day, TimeSpan timeOfDay) =>
        Of(day).Any(w => timeOfDay >= w.Open && timeOfDay <= w.Close);
}
