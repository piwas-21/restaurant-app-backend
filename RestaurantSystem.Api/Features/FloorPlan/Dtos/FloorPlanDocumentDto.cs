namespace RestaurantSystem.Api.Features.FloorPlan.Dtos;

/// <summary>
/// The whole floor-plan document — the single payload the guest map renders and
/// the admin editor saves (FLOOR-PLAN-REVAMP §5.2). <see cref="UpdatedAt"/> is
/// the optimistic-concurrency token: the client sends back the value it loaded,
/// and a mismatch on PUT is a 409.
/// </summary>
public record FloorPlanDocumentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Main floor";
    public decimal WidthMeters { get; set; }
    public decimal HeightMeters { get; set; }
    public int GridSizeCm { get; set; } = 25;
    public string BackgroundStyle { get; set; } = "plain";
    public bool IsDefault { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Concurrency token — echo it back unchanged on save.</summary>
    public DateTime? UpdatedAt { get; set; }

    public List<FloorPlanWallDto> Walls { get; set; } = new();
    public List<FloorPlanItemDto> Items { get; set; } = new();
    public List<FloorPlanTableGeometryDto> Tables { get; set; } = new();
}
