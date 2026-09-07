using System.Linq.Expressions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>
/// What "a reservation occupies table X" means once one booking can span several tables (#561):
/// its PRIMARY table AND every table in <see cref="Reservation.CombinedTables"/>.
/// <para>
/// Every slot-level conflict check goes through <see cref="ConflictsWithAnyOf"/>, so a combined
/// booking blocks each of its tables. The slot reads, enumerated for #561: reservation create, the
/// admin update, the guest self-update, and the available-slots query. The dine-in
/// <c>TableReservation</c> holds (ReservedAt/ReservedUntil — OrderTableReservationService and
/// TableReservationCleanupService) are a different occupancy system keyed on live service, not on
/// a booked slot, and deliberately untouched.
/// </para>
/// </summary>
public static class ReservationSlotOccupancy
{
    /// <summary>
    /// The EF-translatable conflict predicate: some other pending/confirmed booking occupies any
    /// of <paramref name="tableIds"/> on <paramref name="day"/>, overlapping
    /// [<paramref name="start"/>, <paramref name="end"/>). <paramref name="exceptReservationId"/>
    /// removes the reservation being edited from its own conflict check.
    /// </summary>
    public static Expression<Func<Reservation, bool>> ConflictsWithAnyOf(
        IReadOnlyCollection<Guid> tableIds,
        DateTime day,
        TimeSpan start,
        TimeSpan end,
        Guid? exceptReservationId = null) =>
        r => (exceptReservationId == null || r.Id != exceptReservationId) &&
             (tableIds.Contains(r.TableId) || r.CombinedTables.Any(c => tableIds.Contains(c.TableId))) &&
             r.ReservationDate.Date == day.Date &&
             (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
             r.StartTime < end && r.EndTime > start;
}
