namespace RestaurantSystem.Infrastructure.Persistence.Support;

/// <summary>
/// The one source of truth for the legacy-unit → metre table conversion run by
/// the <c>AddFloorPlanAggregate</c> migration (FLOOR-PLAN-REVAMP §6). Kept here
/// (not inline in the migration) so the exact SQL can be exercised by an
/// integration test against real Postgres — the migration itself only ever runs
/// on an install that already had tables, which the test DB never does.
/// </summary>
public static class FloorPlanMigrationSql
{
    /// <summary>Legacy pixel canvas the stored positions were authored on.</summary>
    public const decimal LegacyCanvasWidth = 600m;
    public const decimal LegacyCanvasHeight = 500m;

    /// <summary>Default room the canvas maps onto — 12×10 m keeps today's 6:5 so
    /// nothing moves relative to the room (§6).</summary>
    public const decimal RoomWidthMeters = 12m;
    public const decimal RoomHeightMeters = 10m;

    /// <summary>
    /// Converts every table not yet placed on a plan to metres, remaps its shape
    /// (circle → round; blank → round), and links it to the plan named by the
    /// <c>{PLAN_ID}</c> placeholder. Width = 0 rows (the defect-8 clobber on
    /// demo/staging) fall back to a seats-derived footprint; all others scale by
    /// the canvas → room ratio and clamp to [0.40, 4.00] m. Callers substitute
    /// <c>{PLAN_ID}</c> with a plpgsql variable (migration) or a literal uuid
    /// (test). ROUND(…, 2) keeps the numeric(10,2) columns exact.
    /// </summary>
    public const string ConvertTablesToMetresTemplate = @"
UPDATE ""Tables"" SET
    position_x = ROUND(position_x / 600.0 * 12.0, 2),
    position_y = ROUND(position_y / 500.0 * 10.0, 2),
    width = CASE
        WHEN width = 0 THEN CASE
            WHEN max_guests <= 2 THEN 0.70
            WHEN max_guests <= 4 THEN 1.20
            WHEN max_guests <= 6 THEN 1.80
            ELSE 2.40 END
        ELSE LEAST(GREATEST(ROUND(width / 600.0 * 12.0, 2), 0.40), 4.00) END,
    height = CASE
        WHEN width = 0 THEN CASE
            WHEN max_guests <= 2 THEN 0.70
            WHEN max_guests <= 4 THEN 0.80
            WHEN max_guests <= 6 THEN 0.90
            ELSE 1.00 END
        ELSE LEAST(GREATEST(ROUND(height / 500.0 * 10.0, 2), 0.40), 4.00) END,
    shape = CASE lower(shape)
        WHEN 'circle' THEN 'round'
        WHEN '' THEN 'round'
        ELSE lower(shape) END
    , floor_plan_id = {PLAN_ID}
WHERE floor_plan_id IS NULL;";
}
