using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common.Enums;
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

        // S5. Stored as int with a DEFAULT of 0 (= IngredientKind.Ingredient) so the migration is
        // additive and every existing row keeps today's meaning without a backfill. Deliberately
        // NOT indexed: a product has a handful of ingredients and every read already loads the whole
        // collection, so grouping happens in memory and an index would only cost writes.
        builder.Property(pi => pi.Kind)
            .HasConversion<int>()
            .HasDefaultValue(IngredientKind.Ingredient)
            .IsRequired();

        builder.Property(pi => pi.CreatedAt)
            .IsRequired();

        // UpdatedAt is nullable (set only on update), consistent with every other
        // entity. It was erroneously marked .IsRequired() here, making the column
        // NOT NULL — which broke inserting a ProductIngredient (create/update product
        // with detailed ingredients), since the handlers leave UpdatedAt null on create.

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
