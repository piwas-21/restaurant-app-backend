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
    private const string Round2 = "round_2";
    private const string Round4 = "round_4";
    private const string Square4 = "square_4";
    private const string Rect6 = "rect_6";
    private const string Rect8 = "rect_8";
    private const string Booth4 = "booth_4";

    /// <summary>Footprint presets by table kind (shape, seats, width×height in metres).</summary>
    private static readonly Dictionary<string, (string Shape, int Seats, decimal W, decimal H)> Presets = new()
    {
        [Round2] = ("round", 2, 0.7m, 0.7m),
        [Round4] = ("round", 4, 1.0m, 1.0m),
        [Square4] = ("square", 4, 0.92m, 0.92m),
        [Rect6] = ("rectangle", 6, 1.65m, 0.85m),
        [Rect8] = ("rectangle", 8, 2.3m, 0.95m),
        [Booth4] = ("booth", 4, 1.25m, 0.78m),
    };

    // (number, presetKey, x, y, outdoor, note) — the prototype's reference layout
    // (Main room tables indoor, Terrace tables outdoor).
    private static readonly (string Number, string Preset, decimal X, decimal Y, bool Outdoor, string? Note)[] Layout =
    {
        ("1", Round4, 1.5m, 2.5m, false, null),
        ("2", Round4, 3.5m, 2.5m, false, null),
        ("3", Round4, 5.5m, 2.5m, false, null),
        ("4", Square4, 1.3m, 4.4m, false, "By the fire"),
        ("5", Square4, 1.3m, 6.1m, false, "By the fire"),
        ("6", Rect6, 4.3m, 4.6m, false, "Long table, good for groups"),
        ("7", Round4, 7.0m, 4.7m, false, null),
        ("8", Booth4, 2.1m, 7.3m, false, "Corner booth"),
        ("9", Rect8, 5.2m, 6.6m, false, "Our biggest table"),
        ("10", Round2, 7.6m, 6.3m, false, null),
        ("11a", Round2, 10.4m, 1.1m, true, null),
        ("11b", Round2, 12.6m, 2.4m, true, null),
        ("12a", Round2, 10.4m, 2.6m, true, null),
        ("12b", Round2, 12.6m, 3.9m, true, null),
        ("13a", Round2, 10.4m, 6.2m, true, null),
        ("13b", Round2, 12.6m, 6.3m, true, null),
        ("14a", Round2, 10.4m, 7.7m, true, "Under the tree"),
        ("14b", Round2, 12.6m, 7.7m, true, null),
    };

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
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
                IsOutdoor = row.Outdoor,
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

        var toAdd = existingNumbers.Count == 0
            ? tables
            : tables.Where(t => !existingNumbers.Contains(t.TableNumber)).ToList();

        if (toAdd.Count > 0)
        {
            await context.Tables.AddRangeAsync(toAdd);
            await context.SaveChangesAsync();
        }

        logger.LogInformation(
            "Table seeding complete: {Added} added, {Existing} already present.",
            toAdd.Count, existingNumbers.Count);
    }
}
