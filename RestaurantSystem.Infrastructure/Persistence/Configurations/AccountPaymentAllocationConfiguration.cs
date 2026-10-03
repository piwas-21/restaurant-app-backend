using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountPaymentAllocationConfiguration : IEntityTypeConfiguration<AccountPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<AccountPaymentAllocation> builder)
    {
        builder.ToTable("account_payment_allocations", table => table.HasCheckConstraint(
            "ck_account_payment_allocation_range", "start_ordinal > 0 AND unit_count > 0 AND minor_per_unit > 0"
            + " AND start_ordinal::bigint + unit_count <= 2147483648"
            + " AND (order_item_id IS NOT NULL OR (start_ordinal = 1 AND unit_count = 1))"
            + " AND amount_minor = minor_per_unit * unit_count"));
        builder.HasIndex(value => value.AttemptId);
        builder.HasIndex(value => new { value.OrderId, value.OrderItemId, value.StartOrdinal });
        builder.HasOne(value => value.Attempt).WithMany(value => value.Allocations).HasForeignKey(value => value.AttemptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Order).WithMany().HasForeignKey(value => value.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.OrderItem).WithMany().HasForeignKey(value => value.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.OrderPayment).WithMany().HasForeignKey(value => value.OrderPaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
