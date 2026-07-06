using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders
{
    public static class UserSeeder
    {
        public static async Task SeedAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, ILogger logger, SeedSettings seedSettings)
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

            // Seed Admin User — credentials come from configuration (issue #116);
            // per-tenant provisioning injects them via SeedSettings__* env vars
            // (sofra ADR-003). Creation-only: an existing admin is never modified.
            var adminEmail = seedSettings.AdminEmail;
            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(seedSettings.AdminPassword))
            {
                logger.LogWarning("Admin seeding skipped: SeedSettings.AdminEmail/AdminPassword not configured (roles were still seeded). Set the SeedSettings section in app-secrets.json or SeedSettings__AdminEmail/SeedSettings__AdminPassword env vars to seed an admin on a fresh database.");
                return;
            }

            var existingUser = await userManager.FindByEmailAsync(adminEmail);
            if (existingUser == null)
            {
                var adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = seedSettings.AdminFirstName,
                    LastName = seedSettings.AdminLastName,
                    Role = UserRole.Admin,
                    CreatedBy = "System",
                    CreatedAt = DateTime.UtcNow,
                    RefreshToken = string.Empty, // Initial empty token
                    EmailConfirmed = true,
                    OrderLimitAmount = 0,
                    DiscountPercentage = 0,
                    IsDiscountActive = false
                };

                var result = await userManager.CreateAsync(adminUser, seedSettings.AdminPassword);
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
