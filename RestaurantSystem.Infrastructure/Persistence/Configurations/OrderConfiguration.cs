using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.OrderNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique();

        // 32 random bytes, base64url-encoded => 43 chars. Capped well above that so a future
        // widening is a config change rather than a data migration.
        builder.Property(o => o.QuickActionToken)
            .HasMaxLength(64);

        // Unique so a generator regression that emitted a constant or repeated token fails the
        // INSERT instead of silently making one link open several orders. Postgres treats NULLs
        // as distinct, so the pre-column rows (all null) do not collide with each other.
        builder.HasIndex(o => o.QuickActionToken)
            .IsUnique();

        builder.Property(o => o.CustomerName)
            .HasMaxLength(100);

        builder.Property(o => o.CustomerEmail)
            .HasMaxLength(100);

        builder.Property(o => o.CustomerPhone)
            .HasMaxLength(20);

        builder.Property(o => o.SubTotal)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.Tax)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.DeliveryFee)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.Discount)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.DiscountPercentage)
            .HasColumnType("decimal(5,2)");

        builder.Property(o => o.Tip)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.Total)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.UserLimitAmount)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.PromoCode)
            .HasMaxLength(50);

        builder.Property(o => o.Notes)
            .HasMaxLength(1000);

        builder.Property(o => o.CancellationReason)
            .HasMaxLength(500);

        builder.Property(o => o.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Indexes
        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.OrderDate);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => new { o.UserId, o.OrderDate });

        // Relationships
        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.StatusHistory)
            .WithOne(h => h.Order)
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(o => o.TotalPaid)
        .HasColumnType("decimal(10,2)");

        builder.Property(o => o.RemainingAmount)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.IsFocusOrder)
            .HasDefaultValue(false);

        builder.Property(o => o.Priority)
            .HasDefaultValue(null);

        builder.Property(o => o.FocusReason)
            .HasMaxLength(500);

        builder.Property(o => o.FocusedBy)
            .HasMaxLength(100);

        builder.HasIndex(o => o.IsFocusOrder);
        builder.HasIndex(o => new { o.IsFocusOrder, o.Priority });

    }
}
