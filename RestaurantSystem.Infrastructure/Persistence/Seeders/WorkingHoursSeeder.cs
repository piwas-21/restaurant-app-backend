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
                var open = new TimeSpan(11, 0, 0);
                var close = new TimeSpan(23, 0, 0);

                var hours = new List<WorkingHours>();
                foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
                {
                    var workingHours = new WorkingHours
                    {
                        DayOfWeek = day,
                        // The legacy mirror of the first shift. Seeded beside the shift, never
                        // instead of it — WorkingHoursWindows falls back to this pair when a day
                        // has no shift rows, and a seed that set only the pair would look like a
                        // correctly seeded single-shift day forever.
                        OpenTime = open,
                        CloseTime = close,
                        IsActive = true,
                        IsClosed = false,
                        CreatedBy = "System",
                        CreatedAt = DateTime.UtcNow
                    };

                    // One serving window by default. A tenant that trades a lunch and a dinner
                    // adds the second one in the admin editor; nothing about the default changes.
                    workingHours.Shifts.Add(new WorkingHoursShift
                    {
                        OpenTime = open,
                        CloseTime = close,
                        CreatedBy = "System",
                        CreatedAt = DateTime.UtcNow
                    });

                    hours.Add(workingHours);
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
