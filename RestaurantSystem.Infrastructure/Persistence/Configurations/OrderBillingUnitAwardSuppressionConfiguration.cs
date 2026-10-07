using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingUnitAwardSuppressionConfiguration
    : IEntityTypeConfiguration<OrderBillingUnitAwardSuppression>
{
    public void Configure(EntityTypeBuilder<OrderBillingUnitAwardSuppression> builder)
    {
        builder.ToTable("order_billing_unit_award_suppressions", table => table.HasCheckConstraint(
            "ck_order_billing_unit_award_suppression_points",
            OrderBillingAwardJournalConstraintSql.UnitSuppression));
        builder.HasIndex(value => value.SnapshotUnitId).IsUnique();
        builder.HasIndex(value => value.AmendmentId);
        builder.HasOne<OrderBillingSnapshot>().WithMany()
            .HasForeignKey(value => value.OrderId).HasPrincipalKey(value => value.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshotUnit>().WithMany()
            .HasForeignKey(value => new { value.OrderId, value.SnapshotUnitId })
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
