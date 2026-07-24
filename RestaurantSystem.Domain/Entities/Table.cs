using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class Table : Entity
{
    public string TableNumber { get; set; } = string.Empty;
    public int MaxGuests { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsOutdoor { get; set; }

    // Position of the table CENTRE, in metres (origin top-left, x → right,
    // y → down). Reinterpreted from the legacy 600×500 pixel canvas by the
    // AddFloorPlanAggregate data migration (FLOOR-PLAN-REVAMP §6).
    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    public decimal Width { get; set; } = 80; // Footprint width in metres (post-migration)
    public decimal Height { get; set; } = 80; // Footprint height in metres (post-migration)

    // Shape for visual rendering: round | square | rectangle | booth
    // (legacy "circle" migrated to "round").
    public string Shape { get; set; } = "round";

    // Floor plan this table is placed on. Nullable during migration and for
    // tables created before a plan exists; SetNull on plan delete.
    public Guid? FloorPlanId { get; set; }
    public virtual FloorPlan? FloorPlan { get; set; }

    // Rotation angle in degrees (0-360)
    public int Rotation { get; set; }

    // Admin notes/comments for this table (visible to customers)
    public string? Notes { get; set; }

    // QR Code data for table ordering
    public string? QRCodeData { get; set; }
    public DateTime? QRCodeGeneratedAt { get; set; }

    // Navigation property
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
