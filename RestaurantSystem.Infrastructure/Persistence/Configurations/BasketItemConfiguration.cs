using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class BasketItemConfiguration : IEntityTypeConfiguration<BasketItem>
{
    public void Configure(EntityTypeBuilder<BasketItem> builder)
    {
        builder.ToTable("BasketItems");

        builder.Property(bi => bi.Quantity)
            .IsRequired();

        builder.Property(bi => bi.UnitPrice)
            .HasColumnType("decimal(10,2)");

        builder.Property(bi => bi.ItemTotal)
            .HasColumnType("decimal(10,2)");

        builder.Property(bi => bi.SpecialInstructions)
            .HasMaxLength(500);

        // Customization fields stored as JSON
        builder.Property(bi => bi.SelectedIngredients)
            .HasColumnType("jsonb");

        builder.Property(bi => bi.ExcludedIngredients)
            .HasColumnType("jsonb");

        builder.Property(bi => bi.AddedIngredients)
            .HasColumnType("jsonb");

        builder.Property(bi => bi.CustomizationPrice)
            .HasColumnType("decimal(10,2)");

        builder.HasIndex(bi => bi.BasketId);
        builder.HasIndex(bi => new { bi.BasketId, bi.ProductId, bi.ProductVariationId });

        // Relationship with product
        builder.HasOne(bi => bi.Product)
            .WithMany()
            .HasForeignKey(bi => bi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationship with product variation
        builder.HasOne(bi => bi.ProductVariation)
            .WithMany()
            .HasForeignKey(bi => bi.ProductVariationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(bi => bi.Menu)
           .WithMany()
           .HasForeignKey(bi => bi.MenuId)
           .OnDelete(DeleteBehavior.Restrict);


    }
}
