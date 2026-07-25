namespace RestaurantSystem.Api.Features.FloorPlan;

/// <summary>
/// Allowed vocabulary for the floor-plan document, shared by the validator and
/// the service. The union of the FLOOR-PLAN-REVAMP §5.1 domain list and the
/// prototype's renderer symbol set (the reference the frontend ports from), so
/// the API accepts exactly what the editor emits and the seeder writes.
/// </summary>
public static class FloorPlanKinds
{
    /// <summary>Item <c>kind</c> tokens (structure · decor · wayfinding).</summary>
    public static readonly IReadOnlySet<string> Items = new HashSet<string>(StringComparer.Ordinal)
    {
        // structure
        "bar_counter", "kitchen_pass", "wc", "stairs", "column", "door_free",
        // decor / seating
        "plant", "plant_small", "plant_large", "tree", "fireplace", "rug", "piano",
        "divider", "sofa", "armchair", "banquette", "bar_stool",
        // wayfinding
        "label", "text_label", "zone", "entrance",
    };

    /// <summary>Opening <c>kind</c> tokens.</summary>
    public static readonly IReadOnlySet<string> Openings = new HashSet<string>(StringComparer.Ordinal)
    {
        "door", "window", "opening",
    };

    /// <summary>Table shape tokens.</summary>
    public static readonly IReadOnlySet<string> TableShapes = new HashSet<string>(StringComparer.Ordinal)
    {
        "round", "square", "rectangle", "booth",
    };

    /// <summary>Editor grid sizes in centimetres.</summary>
    public static readonly IReadOnlySet<int> GridSizesCm = new HashSet<int> { 10, 25, 50, 100 };
}
