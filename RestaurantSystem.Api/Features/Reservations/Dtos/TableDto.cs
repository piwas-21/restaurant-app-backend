namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public record TableDto
{
    public Guid Id { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public int MaxGuests { get; set; }
    public bool IsActive { get; set; }
    public bool IsOutdoor { get; set; }
    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public string Shape { get; set; } = "circle";
    public int Rotation { get; set; }
    public string? Notes { get; set; }
    public string? QRCodeData { get; set; }
    public DateTime? QRCodeGeneratedAt { get; set; }

    // Reservation status
    public bool IsReserved { get; set; }
    public DateTime? ReservedUntil { get; set; }

    // Order-based occupancy
    public bool IsOccupied { get; set; }
    public int ActiveOrderCount { get; set; }
    public List<TableOccupantDto>? Occupants { get; set; }
}

public record TableOccupantDto
{
    public string? CustomerName { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public bool IsLoggedInUser { get; set; }
}
