using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");

        builder.Property(i => i.ProductName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.VariationName)
            .HasMaxLength(50);

        builder.Property(i => i.UnitPrice)
            .HasColumnType("decimal(10,2)");

        builder.Property(i => i.ItemTotal)
            .HasColumnType("decimal(10,2)");

        builder.Property(i => i.SpecialInstructions)
            .HasMaxLength(500);

        // Indexes
        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => i.ProductId);

        // Relationships
        builder.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ProductVariation)
            .WithMany()
            .HasForeignKey(i => i.ProductVariationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Menu)
            .WithMany()
            .HasForeignKey(i => i.MenuId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
