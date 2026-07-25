namespace RestaurantSystem.Api.Features.FloorPlan.Dtos;

/// <summary>A wall chain (polyline of vertices in metres); closed chains are rooms.</summary>
public record FloorPlanWallDto
{
    /// <summary>Server id when echoing a stored wall; null for a new one.</summary>
    public Guid? Id { get; set; }

    public List<FloorPlanPointDto> Points { get; set; } = new();
    public decimal ThicknessMeters { get; set; } = 0.12m;
    public bool IsClosed { get; set; }
    public string? RoomName { get; set; }
    public string? FloorStyle { get; set; }
    public int ZIndex { get; set; }

    /// <summary>Doors/windows/gaps pinned to this wall's segments.</summary>
    public List<FloorPlanOpeningDto> Openings { get; set; } = new();
}
