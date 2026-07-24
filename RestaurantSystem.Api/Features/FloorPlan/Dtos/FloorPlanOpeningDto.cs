namespace RestaurantSystem.Api.Features.FloorPlan.Dtos;

/// <summary>A door / window / gap on one segment of its parent wall polyline.</summary>
public record FloorPlanOpeningDto
{
    /// <summary>Server id when echoing a stored opening; null for a new one.</summary>
    public Guid? Id { get; set; }

    public int SegmentIndex { get; set; }
    public decimal OffsetMeters { get; set; }
    public decimal WidthMeters { get; set; }

    /// <summary>"door" | "window" | "opening".</summary>
    public string Kind { get; set; } = "opening";

    /// <summary>Door swing hint (e.g. "in" / "out" / "left" / "right" / "none").</summary>
    public string SwingDirection { get; set; } = "none";
}
