using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductCustomizationIngredientOptionConfiguration : IEntityTypeConfiguration<ProductCustomizationIngredientOption>
{
    public void Configure(EntityTypeBuilder<ProductCustomizationIngredientOption> builder)
    {
        builder.HasOne(option => option.ProductCustomizationGroup)
            .WithMany(group => group.IngredientOptions)
            .HasForeignKey(option => option.ProductCustomizationGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(option => option.ProductIngredient)
            .WithMany(ingredient => ingredient.CustomizationOptionMemberships)
            .HasForeignKey(option => option.ProductIngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(option => option.ProductIngredientId).IsUnique();
        builder.HasIndex(option => new { option.ProductCustomizationGroupId, option.DisplayOrder });
    }
}
