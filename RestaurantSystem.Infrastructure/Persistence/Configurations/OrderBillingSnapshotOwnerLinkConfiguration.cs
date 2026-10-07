using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingSnapshotOwnerLinkConfiguration
    : IEntityTypeConfiguration<OrderBillingSnapshotOwnerLink>
{
    public void Configure(EntityTypeBuilder<OrderBillingSnapshotOwnerLink> builder)
    {
        builder.ToTable("order_billing_snapshot_owner_links", table => table.HasCheckConstraint(
            "ck_order_billing_snapshot_owner_link_shape", OrderBillingSnapshotConstraintSql.OwnerLink));
        builder.Property(value => value.Slot).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.Disposition).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.ErasureTransactionId).HasMaxLength(32);
        builder.HasKey(value => value.Id).HasName("pk_order_billing_snapshot_owner_links");
        builder.HasIndex(value => new { value.OrderId, value.Slot })
            .IsUnique().HasDatabaseName("ix_order_billing_snapshot_owner_links_order_id_slot");
        builder.HasAlternateKey(value => new { value.OrderId, value.Id });
        builder.HasIndex(value => value.UserId)
            .HasDatabaseName("ix_order_billing_snapshot_owner_links_user_id");
        builder.HasOne<OrderBillingSnapshot>().WithMany()
            .HasForeignKey(value => value.OrderId)
            .HasPrincipalKey(value => value.OrderId)
            .HasConstraintName("fk_snapshot_owner_link_snapshot_order")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(value => value.UserId)
            .HasConstraintName("fk_snapshot_owner_link_user")
            .OnDelete(DeleteBehavior.SetNull);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
