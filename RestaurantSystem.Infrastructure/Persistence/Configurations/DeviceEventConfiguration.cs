using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class DeviceEventConfiguration : IEntityTypeConfiguration<DeviceEvent>
{
    public void Configure(EntityTypeBuilder<DeviceEvent> builder)
    {
        builder.ToTable("DeviceEvents");

        builder.Property(e => e.DeviceId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ClientEventId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Level).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Code).HasMaxLength(80);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(2000);
        // Capped plain text, not jsonb: a malformed value must not hard-fail the insert and wedge a
        // retrying outbox (fleet-observability plan). Bounded to keep payloads small.
        builder.Property(e => e.Context).HasMaxLength(4000);

        // Device-scoped idempotency key — a retrying outbox never double-inserts an event.
        builder.HasIndex(e => new { e.DeviceId, e.ClientEventId }).IsUnique();
        // Admin reads list a device's events newest-first.
        builder.HasIndex(e => new { e.DeviceId, e.OccurredAt });
        // The retention sweep purges by ingest time; index it so the 24h sweep never seq-scans.
        builder.HasIndex(e => e.CreatedAt);
    }
}
