using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class Table : Entity
{
    public string TableNumber { get; set; } = string.Empty;
    public int MaxGuests { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsOutdoor { get; set; } = false;

    // Position for visual layout (in pixels or percentage)
    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    public decimal Width { get; set; } = 80; // Default width
    public decimal Height { get; set; } = 80; // Default height

    // Shape for visual rendering: circle, square, rectangle
    public string Shape { get; set; } = "circle";

    // Rotation angle in degrees (0-360)
    public int Rotation { get; set; } = 0;

    // Admin notes/comments for this table (visible to customers)
    public string? Notes { get; set; }

    // QR Code data for table ordering
    public string? QRCodeData { get; set; }
    public DateTime? QRCodeGeneratedAt { get; set; }

    // Navigation property
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
