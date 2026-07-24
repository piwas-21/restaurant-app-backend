using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A door / window / plain gap placed on one segment of a <see cref="FloorPlanWall"/>
/// polyline. Positioned by <see cref="OffsetMeters"/> along segment
/// <see cref="SegmentIndex"/> (from that segment's start vertex) with a given
/// <see cref="WidthMeters"/>, so it stays pinned to the wall even as the wall
/// moves (FLOOR-PLAN-REVAMP §4.3). Doors render a swing arc via
/// <see cref="SwingDirection"/>, windows a double line, plain openings a gap.
/// </summary>
public class FloorPlanOpening : Entity
{
    public Guid WallId { get; set; }
    public virtual FloorPlanWall? Wall { get; set; }

    /// <summary>Zero-based index of the wall segment this opening sits on.</summary>
    public int SegmentIndex { get; set; }

    /// <summary>Distance in metres from the segment's start vertex to the
    /// opening's near edge.</summary>
    public decimal OffsetMeters { get; set; }

    /// <summary>Opening width in metres.</summary>
    public decimal WidthMeters { get; set; }

    /// <summary>"door" | "window" | "opening".</summary>
    public string Kind { get; set; } = "opening";

    /// <summary>Door swing hint (e.g. "in" / "out" / "left" / "right" / "none").</summary>
    public string SwingDirection { get; set; } = "none";
}
