using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders;

public static class TaxConfigurationSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        // Check if any tax configurations exist
        if (await context.TaxConfigurations.AnyAsync())
        {
            return; // Already seeded
        }

        var taxConfiguration = new TaxConfiguration
        {
            Name = "VAT",
            Rate = 0.08m, // 8% tax
            IsEnabled = true,
            Description = "Value Added Tax - Standard Rate",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "System"
        };

        context.TaxConfigurations.Add(taxConfiguration);
        await context.SaveChangesAsync();
    }
}
