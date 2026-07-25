namespace RestaurantSystem.Api.Features.Reservations;

/// <summary>
/// Server-side geometry normalisation for a table created via POST /api/tables
/// (FLOOR-PLAN-REVAMP §5.2). The create endpoint straddles two eras: the
/// still-deployed pixel-canvas frontend posts pixel-scale size/position
/// (width 60–100, x ≈ 260 on the old 600×500 box), while the metre editor
/// posts metres — or omits geometry entirely and places the table via the
/// floor-plan PUT. Because the create handler auto-links the new table to the
/// default plan, its geometry reaches the guest map, so pixel-era values must
/// never be stored as-is. These pure helpers coerce either era into sane
/// metres (§6); they are deliberately DB-free so the branching is unit-tested
/// in isolation.
/// </summary>
public static class TableGeometryDefaults
{
    // A real table is at most a few metres across, and the legacy frontend
    // never posts a size below its 60px minimum — so a supplied size within
    // [MinMetres, MaxPlausibleMetres] is metric and honoured, while anything
    // larger is legacy pixels and discarded for a seats-derived footprint.
    private const decimal MinMetres = 0.1m;
    private const decimal MaxPlausibleMetres = 10m;

    /// <summary>
    /// The table footprint in metres: honour a plausibly-metric supplied size
    /// (clamped inside the plan, mirroring the floor-plan PUT), otherwise
    /// derive it from the seat count (the §6 <c>Width = 0</c> fallback).
    /// </summary>
    public static (decimal Width, decimal Height) MetreFootprint(
        decimal? width, decimal? height, int maxGuests, decimal planWidth, decimal planHeight)
    {
        // Inline relational patterns (not a helper) so the compiler narrows
        // width/height to non-null in the branch — no null-forgiving operator.
        if (width is >= MinMetres and <= MaxPlausibleMetres &&
            height is >= MinMetres and <= MaxPlausibleMetres)
        {
            return (Math.Clamp(width.Value, MinMetres, planWidth), Math.Clamp(height.Value, MinMetres, planHeight));
        }

        var (w, h) = SeatsDerived(maxGuests);
        return (Math.Clamp(w, MinMetres, planWidth), Math.Clamp(h, MinMetres, planHeight));
    }

    /// <summary>
    /// The table centre in metres: honour an in-bounds supplied position,
    /// otherwise centre the table on the plan. Legacy pixel coordinates fall
    /// far outside the metre bounds and so land at the centre for the admin to
    /// place, rather than being clamped onto an edge.
    /// </summary>
    public static (decimal X, decimal Y) MetrePosition(
        decimal? x, decimal? y, decimal planWidth, decimal planHeight)
    {
        var px = x is >= 0m && x <= planWidth ? x.Value : planWidth / 2m;
        var py = y is >= 0m && y <= planHeight ? y.Value : planHeight / 2m;
        return (px, py);
    }

    /// <summary>Legacy shape tokens ("circle" / blank) → the metre-era "round".</summary>
    public static string NormalizeShape(string? shape)
    {
        var normalized = shape?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) || normalized == "circle" ? "round" : normalized;
    }

    // §6 seats → footprint, matching FloorPlanMigrationSql's Width = 0 fallback.
    private static (decimal Width, decimal Height) SeatsDerived(int maxGuests) => maxGuests switch
    {
        <= 2 => (0.70m, 0.70m),
        <= 4 => (1.20m, 0.80m),
        <= 6 => (1.80m, 0.90m),
        _ => (2.40m, 1.00m),
    };
}
