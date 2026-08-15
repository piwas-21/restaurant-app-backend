using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class Reservation : Entity
{
    public Guid? CustomerId { get; set; } // Nullable for guest reservations
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }

    public Guid TableId { get; set; }
    public virtual Table Table { get; set; } = null!;

    /// <summary>
    /// The CALENDAR DAY the guest booked, with <see cref="StartTime"/>/<see cref="EndTime"/> as
    /// wall-clock times on it. NOT an instant: never run it through <c>ITenantClock</c> (#363).
    /// The frontend sends UTC midnight of the chosen day, so converting it would move a real
    /// booking to the previous day for any tenant west of UTC.
    /// </summary>
    public DateTime ReservationDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public int NumberOfGuests { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    public string? SpecialRequests { get; set; }
    public string? Notes { get; set; } // Admin notes

    // Language of this reservation's guest mails, frozen at creation (EMAIL-LOCALISATION-PLAN §1
    // rank 1). The quick-action links carry no request language at all, so on that path this
    // column is the only source there is.
    public string? PreferredLanguage { get; set; }

    // Navigation property for customer (if registered user)
    public virtual ApplicationUser? Customer { get; set; }
}
