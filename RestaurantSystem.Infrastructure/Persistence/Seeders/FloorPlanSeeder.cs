using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seeds the default floor-plan document — the RUMI/demo reference layout from
/// the FLOOR-PLAN-REVAMP prototype (14×9 m: a walled Main room + Terrace, doors
/// and windows, and the decor/structure that lets staff recognise their room).
/// Runs only when <b>no</b> plan exists yet, so it never touches an admin-edited
/// plan and is a no-op on a second boot. On an existing tenant the
/// AddFloorPlanAggregate migration has already created a minimal 12×10 plan from
/// the converted tables, so this seeder is skipped there — the rich reference is
/// for fresh installs (new demo/provisioned tenants). <see cref="TableSeeder"/>
/// runs next and links the seeded tables to this plan.
/// </summary>
public static class FloorPlanSeeder
{
    private const string CreatedBy = "System";

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        if (await context.FloorPlans.AnyAsync())
        {
            logger.LogInformation("Floor plan already present — skipping default-plan seed.");
            return;
        }

        logger.LogInformation("Seeding default floor plan (reference layout)...");

        var plan = new FloorPlan
        {
            Name = "Main floor",
            WidthMeters = 14m,
            HeightMeters = 9m,
            GridSizeCm = 25,
            BackgroundStyle = "plain",
            IsDefault = true,
            DisplayOrder = 0,
            CreatedBy = CreatedBy,
        };

        var mainRoom = new FloorPlanWall
        {
            PointsJson = Points((0.3m, 0.3m), (9.4m, 0.3m), (9.4m, 8.7m), (0.3m, 8.7m)),
            ThicknessMeters = 0.18m,
            IsClosed = true,
            RoomName = "Main room",
            FloorStyle = "wood",
            ZIndex = 0,
            CreatedBy = CreatedBy,
            Openings = new List<FloorPlanOpening>
            {
                Opening(2, 2.2m, 1.2m, "door", "in"),
                Opening(1, 3.4m, 1.7m, "opening"),
                Opening(0, 2.4m, 1.6m, "window"),
                Opening(0, 5.6m, 1.6m, "window"),
            },
        };

        var terrace = new FloorPlanWall
        {
            PointsJson = Points((9.4m, 0.3m), (13.7m, 0.3m), (13.7m, 8.7m), (9.4m, 8.7m)),
            ThicknessMeters = 0.12m,
            IsClosed = true,
            RoomName = "Terrace",
            FloorStyle = "deck",
            ZIndex = 1,
            CreatedBy = CreatedBy,
            Openings = new List<FloorPlanOpening>
            {
                Opening(1, 3.2m, 2.2m, "window"),
                Opening(3, 3.3m, 1.7m, "opening"),
            },
        };

        plan.Walls.Add(mainRoom);
        plan.Walls.Add(terrace);

        var items = new (string Kind, decimal X, decimal Y, decimal W, decimal H, decimal Rot, string? Label)[]
        {
            ("bar_counter", 3.1m, 1.05m, 3.6m, 0.7m, 0m, null),
            ("fireplace", 1.05m, 4.6m, 1.5m, 1.0m, 90m, null),
            ("piano", 8.2m, 1.9m, 1.5m, 2.0m, 18m, null),
            ("banquette", 4.8m, 8.25m, 3.0m, 0.7m, 180m, null),
            ("sofa", 7.9m, 8.1m, 2.0m, 0.9m, 180m, null),
            ("rug", 7.9m, 7.3m, 2.4m, 1.6m, 0m, null),
            ("wc", 8.8m, 3.7m, 1.0m, 1.32m, 0m, null),
            ("plant_large", 6.6m, 1.1m, 0.8m, 0.8m, 0m, null),
            ("plant_small", 0.8m, 2.0m, 0.5m, 0.5m, 0m, null),
            ("plant_small", 9.05m, 6.4m, 0.5m, 0.5m, 0m, null),
            ("tree", 13.0m, 1.15m, 1.2m, 1.28m, 0m, null),
            ("plant_large", 13.05m, 7.9m, 0.8m, 0.8m, 0m, null),
            ("divider", 11.55m, 5.3m, 1.8m, 0.4m, 0m, null),
            ("entrance", 6.6m, 8.2m, 0.9m, 0.6m, 270m, null),
            ("label", 3.1m, 0.45m, 1.2m, 0.34m, 0m, "Bar"),
            ("label", 12.0m, 8.45m, 1.5m, 0.34m, 0m, "Fire exit"),
        };

        for (var i = 0; i < items.Length; i++)
        {
            var (kind, x, y, w, h, rot, label) = items[i];
            plan.Items.Add(new FloorPlanItem
            {
                Kind = kind,
                X = x,
                Y = y,
                WidthMeters = w,
                HeightMeters = h,
                RotationDegrees = rot,
                ZIndex = i,
                Label = label,
                CreatedBy = CreatedBy,
            });
        }

        context.FloorPlans.Add(plan);
        await context.SaveChangesAsync();
        logger.LogInformation("Default floor plan seeded ({Walls} walls, {Items} items).",
            plan.Walls.Count, plan.Items.Count);
    }

    private static string Points(params (decimal X, decimal Y)[] points) =>
        JsonSerializer.Serialize(points.Select(p => new { x = p.X, y = p.Y }));

    private static FloorPlanOpening Opening(int segment, decimal offset, decimal width, string kind, string swing = "none") =>
        new()
        {
            SegmentIndex = segment,
            OffsetMeters = offset,
            WidthMeters = width,
            Kind = kind,
            SwingDirection = swing,
            CreatedBy = CreatedBy,
        };
}
