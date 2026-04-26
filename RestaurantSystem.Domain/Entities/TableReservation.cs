using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Represents a table reservation/occupancy for dine-in orders
/// Tracks when tables are blocked and automatically releases them after timeout
/// </summary>
public class TableReservation : Entity
{
    // Table information
    public Guid TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty; // Denormalized for convenience

    // Order reference (optional - can reserve without order)
    public Guid? OrderId { get; set; }

    // Reservation timing
    public DateTime ReservedAt { get; set; }
    public DateTime ReservedUntil { get; set; }
    public bool IsActive { get; set; } = true;

    // Release tracking
    public DateTime? ReleasedAt { get; set; }
    public string? ReleasedBy { get; set; } // Admin user ID who released manually
    public string? ReleaseReason { get; set; } // "Expired" or "Manual"

    // Navigation properties
    public virtual Table Table { get; set; } = null!;
    public virtual Order? Order { get; set; }
}
