using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentLoyaltyCompensationUnitConfiguration
    : IEntityTypeConfiguration<OrderAmendmentLoyaltyCompensationUnit>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentLoyaltyCompensationUnit> builder)
    {
        builder.ToTable("order_amendment_loyalty_compensation_units", table => table.HasCheckConstraint(
            "ck_order_amendment_loyalty_compensation_unit_points", "points > 0"));
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(value => new { value.SnapshotUnitId, value.Kind }).IsUnique();
        builder.HasIndex(value => new { value.CompensationId, value.SnapshotUnitId }).IsUnique();
        builder.HasOne<OrderAmendmentLoyaltyCompensation>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.CompensationId })
            .HasPrincipalKey(value => new { value.SourceOrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshotUnit>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.SnapshotUnitId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
