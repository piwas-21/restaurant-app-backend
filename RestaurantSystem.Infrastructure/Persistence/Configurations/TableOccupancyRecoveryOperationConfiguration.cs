using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableOccupancyRecoveryOperationConfiguration
    : IEntityTypeConfiguration<TableOccupancyRecoveryOperation>
{
    public void Configure(EntityTypeBuilder<TableOccupancyRecoveryOperation> builder)
    {
        builder.ToTable("table_occupancy_recovery_operations", table => table.HasCheckConstraint(
            "ck_table_occupancy_recovery_operation_shape",
            "expected_readiness_version > 0 AND outcome_readiness_version > 0 "
            + "AND actor_role IN ('Admin', 'Cashier', 'Server') "
            + "AND outcome_readiness_state = 'NeedsReset' "
            + "AND length(request_hash) = 64 AND length(preview_fingerprint) = 64 "
            + "AND length(reason) BETWEEN 1 AND 500 "
            + "AND ((service_session_id IS NULL AND expected_session_version IS NULL "
            + "AND expected_account_revision IS NULL AND outcome_session_version IS NULL "
            + "AND outcome_account_revision IS NULL) OR (service_session_id IS NOT NULL "
            + "AND expected_session_version > 0 AND expected_account_revision > 0 "
            + "AND outcome_session_version > 0 AND outcome_account_revision > 0))"));
        builder.Property(value => value.ActorRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.PreviewFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(value => value.Reason).HasMaxLength(500).IsRequired();
        builder.Property(value => value.OutcomeReadinessState).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(value => value.ExpectedReadinessVersion).IsRequired();
        builder.Property(value => value.OutcomeReadinessVersion).IsRequired();
        builder.Property(value => value.RecordedAt).IsRequired();
        builder.HasIndex(value => new { value.TableId, value.Id }).IsUnique();
        builder.HasIndex(value => value.ServiceSessionId);
        builder.HasOne<Table>().WithMany().HasForeignKey(value => value.TableId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TableServiceSession>().WithMany().HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        SetImmutable(builder);
    }

    private static void SetImmutable(EntityTypeBuilder<TableOccupancyRecoveryOperation> builder)
    {
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
    }
}
