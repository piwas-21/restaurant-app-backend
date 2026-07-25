namespace RestaurantSystem.Api.Features.FloorPlan.Dtos;

/// <summary>
/// A table's identity + geometry as it sits on the plan. The GET payload carries
/// the full set so the guest map renders in one fetch; the PUT applies only the
/// geometry (id + x/y/rotation/shape/size) to existing tables — unknown ids are
/// ignored, and table create/delete/QR stay on the /api/tables endpoints
/// (FLOOR-PLAN-REVAMP §5.2).
/// </summary>
public record FloorPlanTableGeometryDto
{
    public Guid Id { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public int MaxGuests { get; set; }
    public bool IsActive { get; set; }
    public bool IsOutdoor { get; set; }
    public string? Notes { get; set; }

    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public string Shape { get; set; } = "round";
    public int Rotation { get; set; }
}
