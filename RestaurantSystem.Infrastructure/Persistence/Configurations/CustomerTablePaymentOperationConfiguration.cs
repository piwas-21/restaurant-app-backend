using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerTablePaymentOperationConfiguration : IEntityTypeConfiguration<CustomerTablePaymentOperation>
{
    public void Configure(EntityTypeBuilder<CustomerTablePaymentOperation> builder)
    {
        builder.ToTable("customer_table_payment_operations", table => table.HasCheckConstraint(
            "ck_customer_table_payment_operations_outcome",
            "(is_stopped AND attempt_id IS NULL) OR (NOT is_stopped AND attempt_id IS NOT NULL AND attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        builder.HasKey(row => row.OperationId).HasName("pk_customer_table_payment_operations");
        builder.Property(row => row.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(row => row.OwnerHash).HasColumnName("owner_hash").HasMaxLength(64).IsRequired();
        builder.Property(row => row.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        // No account/attempt FK: erasure must never reopen an original financial decision.
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id");
        builder.HasIndex(row => row.AttemptId).IsUnique().HasFilter("attempt_id IS NOT NULL");
        builder.Property(row => row.IsStopped).HasColumnName("is_stopped").HasDefaultValue(false);
        builder.Property(row => row.CompletedAt).HasColumnName("completed_at").IsRequired();
    }
}
