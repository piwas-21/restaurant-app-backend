using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CashierPrintJobConfiguration : IEntityTypeConfiguration<CashierPrintJob>
{
    public void Configure(EntityTypeBuilder<CashierPrintJob> builder)
    {
        builder.ToTable("cashier_print_jobs");
        builder.Property(job => job.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(job => job.Status).HasMaxLength(24).IsRequired();
        builder.Property(job => job.DeviceId).HasMaxLength(128);
        builder.HasIndex(job => new { job.Status, job.CreatedAt });
        builder.HasOne(job => job.Order).WithMany().HasForeignKey(job => job.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(job => job.ServiceSession).WithMany().HasForeignKey(job => job.ServiceSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
