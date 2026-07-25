using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A non-table element placed on a <see cref="FloorPlan"/> — structure
/// (bar counter, WC, stairs, column, kitchen pass), decor (plant, tree,
/// fireplace, rug, piano, divider, sofa, armchair, banquette) or wayfinding
/// (text label, zone region, entrance marker). <see cref="Kind"/> is a string
/// token from the renderer's symbol set (FLOOR-PLAN-REVAMP §4.3 palette); the
/// allowed set lives in <c>FloorPlanKinds</c>. Coordinates are metres; the
/// centre <see cref="X"/>,<see cref="Y"/> is the rotation pivot.
/// </summary>
public class FloorPlanItem : Entity
{
    public Guid FloorPlanId { get; set; }
    public virtual FloorPlan? FloorPlan { get; set; }

    /// <summary>Symbol token (e.g. "bar_counter", "plant_small", "entrance").</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Centre X in metres.</summary>
    public decimal X { get; set; }

    /// <summary>Centre Y in metres.</summary>
    public decimal Y { get; set; }

    public decimal WidthMeters { get; set; }
    public decimal HeightMeters { get; set; }

    /// <summary>Rotation about the centre, degrees in [0, 360).</summary>
    public decimal RotationDegrees { get; set; }

    /// <summary>Stacking order within the item layer.</summary>
    public int ZIndex { get; set; }

    /// <summary>Free text for text-label and zone-region items; null otherwise.</summary>
    public string? Label { get; set; }

    /// <summary>Optional per-kind style token (e.g. floor/finish variant).</summary>
    public string? StyleVariant { get; set; }
}
