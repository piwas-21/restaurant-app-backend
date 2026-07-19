using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class DeviceOrderReceiptConfiguration : IEntityTypeConfiguration<DeviceOrderReceipt>
{
    public void Configure(EntityTypeBuilder<DeviceOrderReceipt> builder)
    {
        builder.ToTable("DeviceOrderReceipts");

        builder.Property(r => r.DeviceId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Target).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.FailureReason).HasMaxLength(500);

        // Natural upsert key — one receipt per order/target/device; makes the outbox idempotent.
        builder.HasIndex(r => new { r.OrderId, r.DeviceId, r.Target }).IsUnique();
        // Reconciliation joins the served set (Orders) to acks on OrderId.
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.DeviceId);
    }
}
