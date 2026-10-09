using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableOccupancyRecoveryDispositionConfiguration
    : IEntityTypeConfiguration<TableOccupancyRecoveryDisposition>
{
    public void Configure(EntityTypeBuilder<TableOccupancyRecoveryDisposition> builder)
    {
        builder.ToTable("table_occupancy_recovery_dispositions", table => table.HasCheckConstraint(
            "ck_table_occupancy_recovery_disposition_shape",
            "((was_legacy_unassigned AND service_session_id IS NULL) "
            + "OR (NOT was_legacy_unassigned AND service_session_id IS NOT NULL)) "
            + "AND (kind <> 'ArchivedLegacyOccupancy' OR was_legacy_unassigned) "
            + "AND (kind <> 'RetainedInPriorVisit' OR NOT was_legacy_unassigned)"));
        builder.Property(value => value.OrderNumber).HasMaxLength(64).IsRequired();
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(value => value.OriginalStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(value => value.OriginalPaymentStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(value => value.OriginalTotal).HasPrecision(10, 2).IsRequired();
        builder.Property(value => value.OriginalBillingCreditAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(value => value.OriginalTotalPaid).HasPrecision(10, 2).IsRequired();
        builder.Property(value => value.OriginalRemainingAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(value => value.RecordedAt).IsRequired();
        builder.HasIndex(value => new { value.OperationId, value.OrderId }).IsUnique();
        builder.HasIndex(value => new { value.TableId, value.OrderId }).IsUnique();
        builder.HasIndex(value => new { value.OrderId, value.WasLegacyUnassigned });
        builder.HasOne<TableOccupancyRecoveryOperation>().WithMany()
            .HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Table>().WithMany().HasForeignKey(value => value.TableId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(value => value.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TableServiceSession>().WithMany().HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
    }
}
