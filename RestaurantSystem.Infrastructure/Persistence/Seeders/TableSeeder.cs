using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

public static class TableSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Seeding tables...");

        // Layout based on actual restaurant floor plan
        // Canvas size: 600x500 pixels
        // Indoor tables (1-10): Circle shape, 4 guests
        // Outdoor tables (11a-14b): Square shape, 2 guests each

        var tables = new List<Table>
        {
            // ========== INDOOR TABLES (1-10) - Circle Shape ==========

            // Table 1
            new Table
            {
                TableNumber = "1",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 80,
                PositionY = 40,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 2
            new Table
            {
                TableNumber = "2",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 160,
                PositionY = 40,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 3
            new Table
            {
                TableNumber = "3",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 40,
                PositionY = 120,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 4
            new Table
            {
                TableNumber = "4",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 40,
                PositionY = 180,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 5
            new Table
            {
                TableNumber = "5",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 40,
                PositionY = 240,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 6
            new Table
            {
                TableNumber = "6",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 40,
                PositionY = 320,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 7
            new Table
            {
                TableNumber = "7",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 140,
                PositionY = 160,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 8
            new Table
            {
                TableNumber = "8",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 140,
                PositionY = 220,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 9
            new Table
            {
                TableNumber = "9",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 260,
                PositionY = 120,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // Table 10
            new Table
            {
                TableNumber = "10",
                MaxGuests = 4,
                IsActive = true,
                IsOutdoor = false,
                Shape = "circle",
                PositionX = 260,
                PositionY = 180,
                Width = 100,
                Height = 100,
                CreatedBy = "System"
            },

            // ========== OUTDOOR TABLES (11a-14b) - Square Shape ==========

            // Tables 11a & 11b
            new Table
            {
                TableNumber = "11a",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 100,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            },

            new Table
            {
                TableNumber = "11b",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 160,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            },

            // Tables 12a & 12b
            new Table
            {
                TableNumber = "12a",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 220,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            },

            new Table
            {
                TableNumber = "12b",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 280,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            },

            // Tables 13a & 13b
            new Table
            {
                TableNumber = "13a",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 340,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            },

            new Table
            {
                TableNumber = "13b",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 400,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            },

            // Tables 14a & 14b
            new Table
            {
                TableNumber = "14a",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 460,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            },

            new Table
            {
                TableNumber = "14b",
                MaxGuests = 2,
                IsActive = true,
                IsOutdoor = true,
                Shape = "square",
                PositionX = 520,
                PositionY = 400,
                Width = 60,
                Height = 60,
                CreatedBy = "System"
            }
        };

        // Check if tables already exist
        var existingTables = await context.Tables.ToListAsync();

        if (existingTables.Any())
        {
            // Tables already exist, only add missing ones (don't overwrite user customizations)
            var missingTables = tables.Where(t => !existingTables.Any(e => e.TableNumber == t.TableNumber)).ToList();

            if (missingTables.Any())
            {
                logger.LogInformation("Adding {Count} missing tables...", missingTables.Count);
                await context.Tables.AddRangeAsync(missingTables);
                await context.SaveChangesAsync();
                logger.LogInformation($"Successfully added {missingTables.Count} missing tables");
            }
            else
            {
                logger.LogInformation("All tables already exist. Skipping seeding to preserve user customizations.");
            }
        }
        else
        {
            // First time seeding - insert all default tables
            await context.Tables.AddRangeAsync(tables);
            await context.SaveChangesAsync();
            logger.LogInformation($"Successfully seeded {tables.Count} tables");
        }
    }
}
