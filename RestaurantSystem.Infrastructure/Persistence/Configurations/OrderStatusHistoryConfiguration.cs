using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory");

        builder.Property(h => h.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(h => h.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(h => h.Notes)
            .HasMaxLength(500);

        builder.Property(h => h.ChangedBy)
            .IsRequired()
            .HasMaxLength(100);

        // Indexes
        builder.HasIndex(h => h.OrderId);
        builder.HasIndex(h => h.ChangedAt);

        // Relationships
        builder.HasOne(h => h.Order)
            .WithMany(o => o.StatusHistory)
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
