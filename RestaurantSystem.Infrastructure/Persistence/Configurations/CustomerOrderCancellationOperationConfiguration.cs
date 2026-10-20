using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerOrderCancellationOperationConfiguration : IEntityTypeConfiguration<CustomerOrderCancellationOperation>
{
    public void Configure(EntityTypeBuilder<CustomerOrderCancellationOperation> builder)
    {
        builder.ToTable("customer_order_cancellation_operations", table => table.HasCheckConstraint(
            "ck_customer_order_cancellation_operation_shape",
            "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid"
            + " AND order_id <> '00000000-0000-0000-0000-000000000000'::uuid"
            + " AND owner_hash ~ '^[0-9a-f]{64}$' AND payload_hash ~ '^[0-9a-f]{64}$'"
            + " AND expected_version > 0 AND observed_version > 0"
            + " AND ((outcome = 'Cancelled' AND reason IS NULL AND observed_version > expected_version)"
            + " OR (outcome = 'Refused' AND reason IS NOT NULL AND reason IN ('OrderChanged', 'NotAwaitingCustomerApproval',"
            + " 'RestaurantManaged', 'FinancialReview', 'OrderingUnavailable')))"));
        builder.HasKey(value => value.OperationId).HasName("pk_customer_order_cancellation_operations");
        builder.Property(value => value.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(value => value.OrderId).HasColumnName("order_id");
        builder.Property(value => value.ExpectedVersion).HasColumnName("expected_version");
        builder.Property(value => value.ObservedVersion).HasColumnName("observed_version");
        builder.Property(value => value.OwnerHash).HasColumnName("owner_hash").HasMaxLength(64).IsRequired();
        builder.Property(value => value.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        builder.Property(value => value.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(value => value.Reason).HasColumnName("reason").HasConversion<string>().HasMaxLength(40);
        builder.Property(value => value.CompletedAt).HasColumnName("completed_at").IsRequired();
        builder.HasIndex(value => value.OrderId).HasDatabaseName("ix_customer_order_cancellation_operations_order_id");
        // No cascade/FK: removing an order cannot release an original cancellation decision.
    }
}
