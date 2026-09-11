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
        builder.Property(r => r.JobType).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.FailureReason).HasMaxLength(500);

        // Legacy clients omit JobId, so retain their natural key exactly. The filtered index keeps
        // a note-update ack for the same order/target from colliding with the old order receipt.
        builder.HasIndex(r => new { r.OrderId, r.DeviceId, r.Target })
            .HasFilter("\"job_id\" IS NULL")
            .IsUnique();
        builder.HasIndex(r => new { r.DeviceId, r.JobId, r.Revision, r.Target })
            .HasFilter("\"job_id\" IS NOT NULL")
            .IsUnique();
        // Reconciliation joins the served set (Orders) to acks on OrderId.
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.DeviceId);
        // The retention sweep purges by ingest time; index it so the 24h sweep never seq-scans.
        builder.HasIndex(r => r.CreatedAt);
    }
}
