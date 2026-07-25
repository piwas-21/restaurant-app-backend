using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A dining-room floor plan in real-world metres (origin top-left, x → right,
/// y → down). Aggregate root for its walls, decor/structure items and the
/// tables placed on it. v1 is single-document per tenant (<see cref="IsDefault"/>
/// marks the one the guest map renders); the multi-room shape is reserved for v2
/// (FLOOR-PLAN-REVAMP §9). <see cref="Common.Base.BaseEntity.UpdatedAt"/> doubles
/// as the optimistic-concurrency token for the whole-document PUT.
/// </summary>
public class FloorPlan : Entity
{
    /// <summary>Human-readable plan name (e.g. "Main floor").</summary>
    public string Name { get; set; } = "Main floor";

    /// <summary>Room width in metres. Changing it adds/removes space at the
    /// right; item coordinates never move.</summary>
    public decimal WidthMeters { get; set; }

    /// <summary>Room height in metres. Changing it adds/removes space at the bottom.</summary>
    public decimal HeightMeters { get; set; }

    /// <summary>Editor grid size in centimetres (10 / 25 / 50 / 100).</summary>
    public int GridSizeCm { get; set; } = 25;

    /// <summary>Background/paper style token consumed by the themed renderer.</summary>
    public string BackgroundStyle { get; set; } = "plain";

    /// <summary>The single plan the anonymous guest map renders.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Ordering for the (v2) multi-plan picker.</summary>
    public int DisplayOrder { get; set; }

    public virtual ICollection<FloorPlanWall> Walls { get; set; } = new List<FloorPlanWall>();
    public virtual ICollection<FloorPlanItem> Items { get; set; } = new List<FloorPlanItem>();
    public virtual ICollection<Table> Tables { get; set; } = new List<Table>();
}
