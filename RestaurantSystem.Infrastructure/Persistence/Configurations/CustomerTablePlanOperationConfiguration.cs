using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerTablePlanOperationConfiguration : IEntityTypeConfiguration<CustomerTablePlanOperation>
{
    public void Configure(EntityTypeBuilder<CustomerTablePlanOperation> builder)
    {
        builder.ToTable("customer_table_plan_operations", table => table.HasCheckConstraint(
            "ck_customer_table_plan_operations_outcome",
            "(is_stopped AND plan_id IS NULL) OR (NOT is_stopped AND plan_id IS NOT NULL AND plan_id <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        builder.HasKey(row => row.OperationId).HasName("pk_customer_table_plan_operations");
        builder.Property(row => row.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(row => row.OwnerHash).HasColumnName("owner_hash").HasMaxLength(64).IsRequired();
        builder.Property(row => row.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        // No account/plan FK: erasure must never reopen an original split decision.
        builder.Property(row => row.PlanId).HasColumnName("plan_id");
        builder.HasIndex(row => row.PlanId).IsUnique().HasFilter("plan_id IS NOT NULL");
        builder.Property(row => row.IsStopped).HasColumnName("is_stopped").HasDefaultValue(false);
        builder.Property(row => row.CompletedAt).HasColumnName("completed_at").IsRequired();
    }
}
