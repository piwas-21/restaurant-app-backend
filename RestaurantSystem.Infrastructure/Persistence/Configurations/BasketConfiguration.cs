using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class BasketConfiguration : IEntityTypeConfiguration<Basket>
{
    public void Configure(EntityTypeBuilder<Basket> builder)
    {
        builder.ToTable("Baskets");

        builder.Property(b => b.SessionId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(b => b.SubTotal)
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.Tax)
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.DeliveryFee)
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.Discount)
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.Total)
            .HasColumnType("decimal(10,2)");

        builder.Property(b => b.PromoCode)
            .HasMaxLength(50);

        builder.Property(b => b.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(b => b.SessionId);
        builder.HasIndex(b => b.UserId);
        builder.HasIndex(b => new { b.SessionId, b.UserId });

        // Relationship with user
        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationship with items
        builder.HasMany(b => b.Items)
            .WithOne(i => i.Basket)
            .HasForeignKey(i => i.BasketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
