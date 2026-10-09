using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountPaymentAllocationReversalConfiguration
    : IEntityTypeConfiguration<AccountPaymentAllocationReversal>
{
    public void Configure(EntityTypeBuilder<AccountPaymentAllocationReversal> builder)
    {
        builder.ToTable("account_payment_allocation_reversals", table => table.HasCheckConstraint(
            "ck_account_payment_allocation_reversal_range",
            "start_ordinal > 0 AND unit_count > 0 AND minor_per_unit > 0"
            + " AND start_ordinal::bigint + unit_count <= 2147483648"
            + " AND (order_item_id IS NOT NULL OR (start_ordinal = 1 AND unit_count = 1))"
            + " AND amount_minor = minor_per_unit * unit_count"));
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.ActorRole).HasMaxLength(30).IsRequired();
        builder.HasIndex(value => new { value.AllocationId, value.StartOrdinal }).IsUnique();
        builder.HasIndex(value => value.RefundLegId);
        builder.HasOne<AccountPaymentAllocation>().WithMany().HasForeignKey(value => value.AllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderAmendmentRefundLeg>().WithMany().HasForeignKey(value => value.RefundLegId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(value => value.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderItem>().WithMany().HasForeignKey(value => value.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
