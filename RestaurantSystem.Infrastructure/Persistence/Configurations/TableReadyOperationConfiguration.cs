using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableReadyOperationConfiguration : IEntityTypeConfiguration<TableReadyOperation>
{
    public void Configure(EntityTypeBuilder<TableReadyOperation> builder)
    {
        builder.ToTable("table_ready_operations", table => table.HasCheckConstraint(
            "ck_table_ready_operation_shape",
            "expected_readiness_version > 0 AND outcome_readiness_version > 0 "
            + "AND actor_role IN ('Admin', 'Cashier', 'Server') "
            + "AND outcome_state IN ('NeedsReset', 'ReadyForGuests') "
            + "AND ((succeeded AND outcome_error_code IS NULL AND outcome_state = 'ReadyForGuests' "
            + "AND outcome_readiness_version::bigint = expected_readiness_version::bigint + 1) "
            + "OR (NOT succeeded AND outcome_error_code IS NOT NULL AND length(outcome_error_code) > 0))"));
        builder.Property(operation => operation.OperationId).IsRequired();
        builder.Property(operation => operation.ActorUserId).IsRequired();
        builder.Property(operation => operation.ActorRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.ExpectedReadinessVersion).IsRequired();
        builder.Property(operation => operation.Succeeded).IsRequired();
        builder.Property(operation => operation.OutcomeErrorCode).HasMaxLength(80);
        builder.Property(operation => operation.OutcomeState).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(operation => operation.OutcomeReadinessVersion).IsRequired();
        builder.Property(operation => operation.RecordedAt).IsRequired();
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.HasIndex(operation => new { operation.TableId, operation.OperationId }).IsUnique();
        builder.HasIndex(operation => operation.TableId);
        builder.HasOne(operation => operation.Table)
            .WithMany(table => table.ReadyOperations)
            .HasForeignKey(operation => operation.TableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
