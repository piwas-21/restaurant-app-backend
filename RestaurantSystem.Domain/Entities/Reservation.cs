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

    public DateTime ReservationDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public int NumberOfGuests { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    public string? SpecialRequests { get; set; }
    public string? Notes { get; set; } // Admin notes

    // Navigation property for customer (if registered user)
    public virtual ApplicationUser? Customer { get; set; }
}
