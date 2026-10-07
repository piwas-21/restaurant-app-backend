using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingAwardUnitCoverageConfiguration : IEntityTypeConfiguration<OrderBillingAwardUnitCoverage>
{
    public void Configure(EntityTypeBuilder<OrderBillingAwardUnitCoverage> builder)
    {
        builder.ToTable("order_billing_award_unit_coverages", table => table.HasCheckConstraint(
            "ck_order_billing_award_unit_coverage_points", "eligible_earned_points > 0"));
        builder.HasIndex(value => value.SnapshotUnitId).IsUnique();
        builder.HasIndex(value => new { value.AwardWitnessId, value.SnapshotUnitId }).IsUnique();
        builder.HasOne<OrderBillingAwardWitness>().WithMany()
            .HasForeignKey(value => new { value.OrderId, value.AwardWitnessId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshotUnit>().WithMany()
            .HasForeignKey(value => new { value.OrderId, value.SnapshotUnitId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
