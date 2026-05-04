using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders
{
    public static class WorkingHoursSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
        {
            if (!await context.Set<WorkingHours>().AnyAsync())
            {
                var hours = new List<WorkingHours>();
                foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
                {
                    hours.Add(new WorkingHours
                    {
                        DayOfWeek = day,
                        OpenTime = new TimeSpan(11, 0, 0),
                        CloseTime = new TimeSpan(23, 0, 0),
                        IsActive = true,
                        IsClosed = false,
                        CreatedBy = "System",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await context.Set<WorkingHours>().AddRangeAsync(hours);
                await context.SaveChangesAsync();
                logger.LogInformation("Working hours seeded successfully.");
            }
            else
            {
                logger.LogInformation("Working hours already exist.");
            }
        }
    }
}
