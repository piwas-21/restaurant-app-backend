using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Common;

public static class TestDataSeeder
{
    public static async Task SeedBasicDataAsync(ApplicationDbContext context)
    {
        // Check if data already exists
        if (await context.Products.AnyAsync())
        {
            return;
        }

        // Seed the test users referenced by TestAuthHandler. The handler
        // synthesizes a Customer principal on every request (with the admin
        // override surfaced via X-Test-Admin), so any code path that reads
        // ICurrentUserService.UserId — Basket creation, Order creation —
        // sees these IDs even when the test "stayed anonymous". Without
        // these rows, FK constraints on Baskets/Orders → AspNetUsers fail.
        await SeedTestUsersAsync(context);

        // Seed categories
        var categories = new List<Category>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Main Course",
                DisplayOrder = 1,
                IsActive = true,
                CreatedBy = "seed",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Beverages",
                DisplayOrder = 2,
                IsActive = true,
                CreatedBy = "seed",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Desserts",
                DisplayOrder = 3,
                IsActive = true,
                CreatedBy = "seed",
                CreatedAt = DateTime.UtcNow
            }
        };

        context.Categories.AddRange(categories);

        // Seed products
        var products = new List<Product>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Test Pizza",
                Description = "Delicious test pizza",
                BasePrice = 12.99m,
                IsActive = true,
                IsAvailable = true,
                PreparationTimeMinutes = 20,
                Type = ProductType.MainItem,
                Ingredients = new List<string> { "Dough", "Cheese", "Tomato" },
                Allergens = new List < string > { "Gluten", "Dairy" },
                DisplayOrder = 1,
                CreatedBy = "seed",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Test Cola",
                Description = "Refreshing cola",
                BasePrice = 2.99m,
                IsActive = true,
                IsAvailable = true,
                PreparationTimeMinutes = 0,
                Type = ProductType.Beverage,
                Ingredients = new List < string > { "Water", "Sugar", "CO2" },
                Allergens = new List < string >(),
                DisplayOrder = 1,
                CreatedBy = "seed",
                CreatedAt = DateTime.UtcNow
            }
        };

        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    private static async Task SeedTestUsersAsync(ApplicationDbContext context)
    {
        var now = DateTime.UtcNow;

        var users = new List<ApplicationUser>
        {
            new()
            {
                Id = Guid.Parse(TestAuthHandler.UserId),
                UserName = TestAuthHandler.UserName,
                NormalizedUserName = TestAuthHandler.UserName.ToUpperInvariant(),
                Email = TestAuthHandler.UserName,
                NormalizedEmail = TestAuthHandler.UserName.ToUpperInvariant(),
                EmailConfirmed = true,
                FirstName = "Test",
                LastName = "User",
                Role = UserRole.Customer,
                CreatedAt = now,
                CreatedBy = "seed",
                RefreshToken = string.Empty,
                SecurityStamp = Guid.NewGuid().ToString()
            },
            new()
            {
                Id = Guid.Parse(TestAuthHandler.AdminUserId),
                UserName = TestAuthHandler.AdminUserName,
                NormalizedUserName = TestAuthHandler.AdminUserName.ToUpperInvariant(),
                Email = TestAuthHandler.AdminUserName,
                NormalizedEmail = TestAuthHandler.AdminUserName.ToUpperInvariant(),
                EmailConfirmed = true,
                FirstName = "Admin",
                LastName = "User",
                Role = UserRole.Admin,
                CreatedAt = now,
                CreatedBy = "seed",
                RefreshToken = string.Empty,
                SecurityStamp = Guid.NewGuid().ToString()
            }
        };

        context.Users.AddRange(users);
        await context.SaveChangesAsync();
    }
}
