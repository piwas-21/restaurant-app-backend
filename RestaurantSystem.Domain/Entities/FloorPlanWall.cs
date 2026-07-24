using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A wall chain on a <see cref="FloorPlan"/>: an ordered polyline of vertices in
/// metres. A chain closed onto its first vertex (<see cref="IsClosed"/>) encloses
/// a <em>room</em> — it gets a <see cref="RoomName"/> and a <see cref="FloorStyle"/>
/// (FLOOR-PLAN-REVAMP §4.3). Doors/windows are <see cref="FloorPlanOpening"/>s
/// pinned to a segment of this chain, so they can never float off the wall.
/// </summary>
public class FloorPlanWall : Entity
{
    public Guid FloorPlanId { get; set; }
    public virtual FloorPlan? FloorPlan { get; set; }

    /// <summary>Vertices as a JSON array of <c>{ "x": m, "y": m }</c> in metres
    /// (≤ 200), stored as jsonb. Kept as serialized text rather than an owned
    /// collection so the whole-document PUT round-trips one value with no
    /// per-vertex change-tracking.</summary>
    public string PointsJson { get; set; } = "[]";

    /// <summary>Wall thickness in metres.</summary>
    public decimal ThicknessMeters { get; set; } = 0.12m;

    /// <summary>True when the last vertex joins the first — the chain is a room.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Room name for a closed chain; null for an open wall run.</summary>
    public string? RoomName { get; set; }

    /// <summary>Floor finish token for a room (wood / tile / stone / carpet / deck).</summary>
    public string? FloorStyle { get; set; }

    /// <summary>Stacking order within the wall layer.</summary>
    public int ZIndex { get; set; }

    public virtual ICollection<FloorPlanOpening> Openings { get; set; } = new List<FloorPlanOpening>();
}
