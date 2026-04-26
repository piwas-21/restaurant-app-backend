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
}
