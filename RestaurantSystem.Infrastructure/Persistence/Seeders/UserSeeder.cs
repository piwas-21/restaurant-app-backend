using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders
{
    public static class UserSeeder
    {
        public static async Task SeedAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, ILogger logger)
        {
            // Seed Roles
            foreach (var roleName in Enum.GetNames(typeof(UserRole)))
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                    logger.LogInformation($"Role '{roleName}' created.");
                }
            }

            // Seed Admin User
            var adminEmail = "admin@email.com";
            var existingUser = await userManager.FindByEmailAsync(adminEmail);
            if (existingUser == null)
            {
                var adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "Admin",
                    LastName = "User",
                    Role = UserRole.Admin,
                    CreatedBy = "System",
                    CreatedAt = DateTime.UtcNow,
                    RefreshToken = string.Empty, // Initial empty token
                    EmailConfirmed = true,
                    OrderLimitAmount = 0,
                    DiscountPercentage = 0,
                    IsDiscountActive = false
                };

                var result = await userManager.CreateAsync(adminUser, "Rumi.123");
                if (result.Succeeded)
                {
                    logger.LogInformation($"Admin user '{adminEmail}' created successfully.");
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
                else
                {
                    logger.LogError($"Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
            else
            {
                logger.LogInformation($"Admin user '{adminEmail}' already exists.");
            }
        }
    }
}
