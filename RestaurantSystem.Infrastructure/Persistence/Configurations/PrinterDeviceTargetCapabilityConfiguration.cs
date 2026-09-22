using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class PrinterDeviceTargetCapabilityConfiguration
    : IEntityTypeConfiguration<PrinterDeviceTargetCapability>
{
    public void Configure(EntityTypeBuilder<PrinterDeviceTargetCapability> builder)
    {
        builder.ToTable("PrinterDeviceTargetCapabilities");
        builder.Property(capability => capability.DeviceId).IsRequired().HasMaxLength(64);
        builder.Property(capability => capability.Target).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(capability => capability.PrinterName).HasMaxLength(120);
        builder.HasIndex(capability => new { capability.DeviceId, capability.Target }).IsUnique();
        builder.HasIndex(capability => new { capability.Target, capability.ReportedAt });
    }
}
