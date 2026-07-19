using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class PrinterDeviceConfiguration : IEntityTypeConfiguration<PrinterDevice>
{
    public void Configure(EntityTypeBuilder<PrinterDevice> builder)
    {
        builder.ToTable("PrinterDevices");

        builder.Property(d => d.DeviceId).IsRequired().HasMaxLength(64);
        builder.Property(d => d.Label).HasMaxLength(120);
        builder.Property(d => d.TenantSlug).HasMaxLength(80);
        builder.Property(d => d.Platform).HasMaxLength(40);
        builder.Property(d => d.AppVersion).HasMaxLength(40);
        builder.Property(d => d.ApiBaseUrl).HasMaxLength(300);
        builder.Property(d => d.KitchenPrinter).HasMaxLength(120);
        builder.Property(d => d.CashierPrinter).HasMaxLength(120);

        // The X-Device-Id is the natural upsert key — one row per installation.
        builder.HasIndex(d => d.DeviceId).IsUnique();
        // Fleet views filter/sort by tenant and liveness.
        builder.HasIndex(d => d.TenantSlug);
        builder.HasIndex(d => d.LastHeartbeatAt);
    }
}
