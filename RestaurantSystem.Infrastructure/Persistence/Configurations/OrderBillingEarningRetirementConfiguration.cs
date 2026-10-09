using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingEarningRetirementConfiguration
    : IEntityTypeConfiguration<OrderBillingEarningRetirement>
{
    public void Configure(EntityTypeBuilder<OrderBillingEarningRetirement> builder)
    {
        builder.ToTable("order_billing_earning_retirements", table => table.HasCheckConstraint(
            "ck_order_billing_earning_retirement_values",
            "retired_unit_count > 0 AND created_by = 'OrderBillingEarningRetirementService' AND updated_at IS NULL AND updated_by IS NULL"));
        builder.HasIndex(value => value.OrderId).IsUnique();
        builder.HasOne<Order>().WithMany().HasForeignKey(value => value.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshot>().WithMany()
            .HasForeignKey(value => new { value.OrderId, value.SnapshotId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderAmendment>().WithMany()
            .HasForeignKey(value => new { value.OrderId, value.AmendmentId })
            .HasPrincipalKey(value => new { value.SourceOrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
