using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingSnapshotConfiguration : IEntityTypeConfiguration<OrderBillingSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderBillingSnapshot> builder)
    {
        builder.ToTable("order_billing_snapshots", table => table.HasCheckConstraint(
            "ck_order_billing_snapshot_values", OrderBillingSnapshotConstraintSql.Header));
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.PricingPolicyVersion).HasMaxLength(40).IsRequired();
        builder.Property(value => value.ComponentQuantizationPolicyVersion).HasMaxLength(60).IsRequired();
        builder.Property(value => value.EarningBasisPolicyVersion).HasMaxLength(50).IsRequired();
        builder.Property(value => value.RawTaxAmount).HasColumnType("numeric").IsRequired();
        builder.Property(value => value.RawOrderDiscountAmount).HasColumnType("numeric").IsRequired();
        builder.Property(value => value.RawCustomerDiscountAmount).HasColumnType("numeric").IsRequired();
        builder.Property(value => value.RawRedemptionDiscountAmount).HasColumnType("numeric").IsRequired();
        builder.Property(value => value.RawCourtesyRoundingAmount).HasColumnType("numeric").IsRequired();
        builder.Property(value => value.EarningEvaluationVersion).HasMaxLength(80);
        builder.Property(value => value.EarningRuleSetFingerprint).HasMaxLength(64);
        builder.Property(value => value.EarningRuleName).HasMaxLength(100);
        builder.Property(value => value.RedemptionTransactionType).HasConversion<string>().HasMaxLength(30);
        builder.Property(value => value.RedemptionTransactionOrderTotal).HasColumnType("decimal(18,2)");
        builder.Property(value => value.TaxCategory).HasMaxLength(30).IsRequired();
        builder.Property(value => value.TaxTreatment).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasAlternateKey(value => value.OrderId);
        builder.HasAlternateKey(value => new { value.OrderId, value.Id });
        builder.HasIndex(value => value.RedemptionTransactionId)
            .IsUnique()
            .HasFilter("\"redemption_transaction_id\" IS NOT NULL");
        builder.HasOne<Order>().WithMany().HasForeignKey(value => value.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        MakeImmutable(builder);
    }

    private static void MakeImmutable(EntityTypeBuilder<OrderBillingSnapshot> builder)
    {
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
