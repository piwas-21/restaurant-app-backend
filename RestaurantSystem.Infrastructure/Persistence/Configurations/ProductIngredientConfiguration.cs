using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductIngredientConfiguration : IEntityTypeConfiguration<ProductIngredient>
{
    public void Configure(EntityTypeBuilder<ProductIngredient> builder)
    {
        builder.ToTable("ProductIngredients");

        builder.HasKey(pi => pi.Id);

        builder.Property(pi => pi.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(pi => pi.Price)
            .HasColumnType("decimal(18,2)");

        builder.Property(pi => pi.IsOptional)
            .IsRequired();

        builder.Property(pi => pi.IsActive)
            .IsRequired();

        builder.Property(pi => pi.DisplayOrder)
            .IsRequired();

        builder.Property(pi => pi.CreatedAt)
            .IsRequired();

        builder.Property(pi => pi.UpdatedAt)
            .IsRequired();

        // Relationship with Product
        builder.HasOne(pi => pi.Product)
            .WithMany(p => p.DetailedIngredients)
            .HasForeignKey(pi => pi.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Relationship with Descriptions
        builder.HasMany(pi => pi.Descriptions)
            .WithOne(d => d.ProductIngredient)
            .HasForeignKey(d => d.ProductIngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(pi => pi.ProductId);
        builder.HasIndex(pi => new { pi.ProductId, pi.DisplayOrder });
    }
}
