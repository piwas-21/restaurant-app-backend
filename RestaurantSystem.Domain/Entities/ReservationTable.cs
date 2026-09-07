using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// One NON-PRIMARY table a reservation occupies. A combined booking over N tables is ONE
/// <see cref="Reservation"/> — the primary table stays on <c>Reservation.TableId</c> — with one
/// row here per additional table (#561). The party's capacity is the SUM of the primary and every
/// combined table's MaxGuests, and every slot-occupancy read ("is table X free at slot T") counts
/// a reservation as occupying its combined tables too.
/// </summary>
public class ReservationTable : Entity
{
    public Guid ReservationId { get; set; }
    public virtual Reservation Reservation { get; set; } = null!;

    public Guid TableId { get; set; }
    public virtual Table Table { get; set; } = null!;
}
