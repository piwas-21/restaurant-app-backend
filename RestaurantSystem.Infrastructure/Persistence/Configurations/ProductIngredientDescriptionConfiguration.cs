using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductIngredientDescriptionConfiguration : IEntityTypeConfiguration<ProductIngredientDescription>
{
    public void Configure(EntityTypeBuilder<ProductIngredientDescription> builder)
    {
        builder.ToTable("ProductIngredientDescriptions");

        builder.HasKey(pid => pid.Id);

        builder.Property(pid => pid.LanguageCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(pid => pid.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(pid => pid.Description)
            .HasMaxLength(500);

        // Relationship with ProductIngredient
        builder.HasOne(pid => pid.ProductIngredient)
            .WithMany(pi => pi.Descriptions)
            .HasForeignKey(pid => pid.ProductIngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint: one description per language per ingredient
        builder.HasIndex(pid => new { pid.ProductIngredientId, pid.LanguageCode })
            .IsUnique();
    }
}
