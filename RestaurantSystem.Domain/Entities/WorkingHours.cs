using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class WorkingHours : Entity
{
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>
    /// Opening time of the day's FIRST shift. Kept as a maintained mirror of
    /// <c>Shifts.OrderBy(OpenTime).First()</c>, not as a second source of truth: see
    /// <see cref="Shifts"/> for why the column survives at all.
    /// </summary>
    public TimeSpan OpenTime { get; set; }

    /// <summary>Closing time of the day's FIRST shift. Mirror, as <see cref="OpenTime"/>.</summary>
    public TimeSpan CloseTime { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsClosed { get; set; } // For special closed days
    public string? Notes { get; set; }

    /// <summary>
    /// The day's serving windows, in no guaranteed order — read them through
    /// <c>OrderBy(s =&gt; s.OpenTime)</c>. This is the source of truth for "is the shop open".
    /// <para>
    /// <b>Why <see cref="OpenTime"/>/<see cref="CloseTime"/> still exist.</b> They were the source
    /// of truth before shifts, and the migration that introduced this collection is purely
    /// additive: it creates a table and backfills it, and drops no column. That buys two things on
    /// a platform with a live tenant. (1) Rollback: an older binary reading the old columns is
    /// still CORRECT for every single-shift tenant, with no data restore. (2) The published
    /// <c>WorkingHoursDto</c> contract, which a mobile client outside this repository reads —
    /// pinning the mirror to the FIRST shift makes an un-updated client UNDER-report the day
    /// (it misses dinner) rather than OVER-report it (claiming the shop is open through the
    /// closure), and under-reporting is the failure that does not lie to a customer.
    /// </para>
    /// <para>
    /// The mirror has exactly one writer, <c>WorkingHoursService.UpdateAsync</c>. Nothing else may
    /// set <see cref="OpenTime"/> or <see cref="CloseTime"/> without setting the shifts beside it.
    /// </para>
    /// </summary>
    public ICollection<WorkingHoursShift> Shifts { get; set; } = new List<WorkingHoursShift>();
}
