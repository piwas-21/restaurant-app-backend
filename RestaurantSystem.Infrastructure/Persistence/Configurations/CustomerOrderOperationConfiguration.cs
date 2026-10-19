using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerOrderOperationConfiguration : IEntityTypeConfiguration<CustomerOrderOperation>
{
    public void Configure(EntityTypeBuilder<CustomerOrderOperation> builder)
    {
        builder.ToTable("customer_order_operations", table => table.HasCheckConstraint(
            "ck_customer_order_operations_outcome",
            "(is_cancelled AND order_id IS NULL) OR (NOT is_cancelled AND order_id IS NOT NULL AND order_id <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        builder.HasKey(operation => operation.OperationId).HasName("pk_customer_order_operations");
        builder.Property(operation => operation.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(operation => operation.OwnerHash).HasColumnName("owner_hash").HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        // No cascade/FK: removal of a historical order must never allow its operation to execute again.
        builder.Property(operation => operation.OrderId).HasColumnName("order_id");
        builder.Property(operation => operation.IsCancelled).HasColumnName("is_cancelled");
        builder.Property(operation => operation.CompletedAt).HasColumnName("completed_at").IsRequired();
    }
}
