using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Persistence.Seeders;


namespace RestaurantSystem.Infrastructure.Extensions
{
    public static class MigrationExtensions
    {
        public static async Task MigrateApplicationDatabaseAsync(this IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

            try
            {
                logger.LogInformation("Applying migrations for database");
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Migrations successfully applied");

                // Seed fidelity points data
                logger.LogInformation("Seeding fidelity points data");
                await FidelityPointsSeeder.SeedAsync(dbContext);
                logger.LogInformation("Fidelity points data seeded successfully");

                // Seed tables data
                logger.LogInformation("Seeding tables data");
                await TableSeeder.SeedAsync(dbContext, logger);
                logger.LogInformation("Tables data seeded successfully");

                // Seed global ingredients data
                logger.LogInformation("Seeding global ingredients data");
                await GlobalIngredientsSeeder.SeedAsync(dbContext, logger);
                logger.LogInformation("Global ingredients data seeded successfully");

                // Seed users and roles
                logger.LogInformation("Seeding users and roles");
                await UserSeeder.SeedAsync(userManager, roleManager, logger);
                logger.LogInformation("Users and roles seeded successfully");

                // Seed working hours
                logger.LogInformation("Seeding working hours");
                await WorkingHoursSeeder.SeedAsync(dbContext, logger);
                logger.LogInformation("Working hours seeded successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while applying migrations or seeding data");
                throw;
            }
        }

        public static async Task EnsureDatabaseCreatedAsync(this IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

            try
            {
                if (!await dbContext.Database.CanConnectAsync())
                {
                    logger.LogInformation("Creating database as it doesn't exist");
                    await dbContext.Database.EnsureCreatedAsync();
                    logger.LogInformation("Database created successfully");
                }
                else
                {
                    logger.LogInformation("Database connection verified");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while ensuring database exists");
                throw;
            }
        }
    }
}
