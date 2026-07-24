using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seeds the reference dining tables in real-world metres (FLOOR-PLAN-REVAMP §6),
/// matching the prototype's 14×9 m layout, and links them to the default plan
/// seeded by <see cref="FloorPlanSeeder"/> (which must run first). Preserves the
/// legacy behaviour: on an existing install only genuinely missing table numbers
/// are added, so admin customisations and the migration's in-place unit→metre
/// conversion of existing rows are never overwritten.
/// </summary>
public static class TableSeeder
{
    /// <summary>Footprint presets by table kind (shape, seats, width×height in metres).</summary>
    private static readonly Dictionary<string, (string Shape, int Seats, decimal W, decimal H)> Presets = new()
    {
        ["round_2"] = ("round", 2, 0.7m, 0.7m),
        ["round_4"] = ("round", 4, 1.0m, 1.0m),
        ["square_4"] = ("square", 4, 0.92m, 0.92m),
        ["rect_6"] = ("rectangle", 6, 1.65m, 0.85m),
        ["rect_8"] = ("rectangle", 8, 2.3m, 0.95m),
        ["booth_4"] = ("booth", 4, 1.25m, 0.78m),
    };

    // (number, presetKey, x, y, zone, note) — the prototype's reference layout.
    private static readonly (string Number, string Preset, decimal X, decimal Y, string Zone, string? Note)[] Layout =
    {
        ("1", "round_4", 1.5m, 2.5m, "Main room", null),
        ("2", "round_4", 3.5m, 2.5m, "Main room", null),
        ("3", "round_4", 5.5m, 2.5m, "Main room", null),
        ("4", "square_4", 1.3m, 4.4m, "Main room", "By the fire"),
        ("5", "square_4", 1.3m, 6.1m, "Main room", "By the fire"),
        ("6", "rect_6", 4.3m, 4.6m, "Main room", "Long table, good for groups"),
        ("7", "round_4", 7.0m, 4.7m, "Main room", null),
        ("8", "booth_4", 2.1m, 7.3m, "Main room", "Corner booth"),
        ("9", "rect_8", 5.2m, 6.6m, "Main room", "Our biggest table"),
        ("10", "round_2", 7.6m, 6.3m, "Main room", null),
        ("11a", "round_2", 10.4m, 1.1m, "Terrace", null),
        ("11b", "round_2", 12.6m, 2.4m, "Terrace", null),
        ("12a", "round_2", 10.4m, 2.6m, "Terrace", null),
        ("12b", "round_2", 12.6m, 3.9m, "Terrace", null),
        ("13a", "round_2", 10.4m, 6.2m, "Terrace", null),
        ("13b", "round_2", 12.6m, 6.3m, "Terrace", null),
        ("14a", "round_2", 10.4m, 7.7m, "Terrace", "Under the tree"),
        ("14b", "round_2", 12.6m, 7.7m, "Terrace", null),
    };

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Seeding tables...");

        var defaultPlanId = await context.FloorPlans
            .Where(p => p.IsDefault)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync();

        var tables = Layout.Select(row =>
        {
            var preset = Presets[row.Preset];
            return new Table
            {
                TableNumber = row.Number,
                MaxGuests = preset.Seats,
                IsActive = true,
                IsOutdoor = row.Zone == "Terrace",
                Shape = preset.Shape,
                PositionX = row.X,
                PositionY = row.Y,
                Width = preset.W,
                Height = preset.H,
                Rotation = 0,
                Notes = row.Note,
                FloorPlanId = defaultPlanId,
                CreatedBy = "System",
            };
        }).ToList();

        var existingNumbers = await context.Tables
            .Select(t => t.TableNumber)
            .ToListAsync();

        if (existingNumbers.Count == 0)
        {
            await context.Tables.AddRangeAsync(tables);
            await context.SaveChangesAsync();
            logger.LogInformation("Successfully seeded {Count} tables", tables.Count);
            return;
        }

        var missing = tables.Where(t => !existingNumbers.Contains(t.TableNumber)).ToList();
        if (missing.Count == 0)
        {
            logger.LogInformation("All tables already exist. Skipping seeding to preserve customizations.");
            return;
        }

        logger.LogInformation("Adding {Count} missing tables...", missing.Count);
        await context.Tables.AddRangeAsync(missing);
        await context.SaveChangesAsync();
        logger.LogInformation("Successfully added {Count} missing tables", missing.Count);
    }
}
