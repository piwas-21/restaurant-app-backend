namespace RestaurantSystem.Api.Features.FloorPlan.Dtos;

/// <summary>A structure / decor / wayfinding element on the plan (metres).</summary>
public record FloorPlanItemDto
{
    /// <summary>Server id when echoing a stored item; null for a new one.</summary>
    public Guid? Id { get; set; }

    public string Kind { get; set; } = string.Empty;
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal WidthMeters { get; set; }
    public decimal HeightMeters { get; set; }
    public decimal RotationDegrees { get; set; }
    public int ZIndex { get; set; }
    public string? Label { get; set; }
    public string? StyleVariant { get; set; }
}
