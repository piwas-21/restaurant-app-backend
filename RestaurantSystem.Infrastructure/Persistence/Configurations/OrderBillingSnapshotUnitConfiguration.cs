using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingSnapshotUnitConfiguration : IEntityTypeConfiguration<OrderBillingSnapshotUnit>
{
    public void Configure(EntityTypeBuilder<OrderBillingSnapshotUnit> builder)
    {
        builder.ToTable("order_billing_snapshot_units", table => table.HasCheckConstraint(
            "ck_order_billing_snapshot_unit_values", OrderBillingSnapshotConstraintSql.Unit));
        builder.Property(value => value.TaxCategory).HasMaxLength(30).IsRequired();
        builder.Property(value => value.TaxTreatment).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasAlternateKey(value => new { value.OrderId, value.Id });
        builder.HasIndex(value => new { value.OrderId, value.OrderItemId, value.UnitOrdinal }).IsUnique();
        builder.HasOne<OrderBillingSnapshot>().WithMany().HasForeignKey(value => value.OrderId)
            .HasPrincipalKey(value => value.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderItem>().WithMany()
            .HasForeignKey(value => new { value.OrderId, value.OrderItemId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
