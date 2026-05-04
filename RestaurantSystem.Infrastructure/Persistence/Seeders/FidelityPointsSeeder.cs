using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

public static class FidelityPointsSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        // Check if point earning rules already exist
        if (await context.PointEarningRules.AnyAsync())
        {
            return; // Already seeded
        }

        var pointEarningRules = new List<PointEarningRule>
        {
            new()
            {
                Name = "Bronze Level",
                MinOrderAmount = 0,
                MaxOrderAmount = 20,
                PointsAwarded = 5,
                IsActive = true,
                Priority = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },
            new()
            {
                Name = "Silver Level",
                MinOrderAmount = 20.01m,
                MaxOrderAmount = 50,
                PointsAwarded = 15,
                IsActive = true,
                Priority = 2,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },
            new()
            {
                Name = "Gold Level",
                MinOrderAmount = 50.01m,
                MaxOrderAmount = 100,
                PointsAwarded = 30,
                IsActive = true,
                Priority = 3,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            },
            new()
            {
                Name = "Platinum Level",
                MinOrderAmount = 100.01m,
                MaxOrderAmount = null, // No upper limit
                PointsAwarded = 60,
                IsActive = true,
                Priority = 4,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            }
        };

        await context.PointEarningRules.AddRangeAsync(pointEarningRules);
        await context.SaveChangesAsync();
    }
}
