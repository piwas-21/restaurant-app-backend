using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentLoyaltyCompensationConfiguration
    : IEntityTypeConfiguration<OrderAmendmentLoyaltyCompensation>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentLoyaltyCompensation> builder)
    {
        builder.ToTable("order_amendment_loyalty_compensations", table => table.HasCheckConstraint(
            "ck_order_amendment_loyalty_compensation_points",
            "source_order_id <> '00000000-0000-0000-0000-000000000000'::uuid AND snapshot_id <> '00000000-0000-0000-0000-000000000000'::uuid AND owner_link_id <> '00000000-0000-0000-0000-000000000000'::uuid AND original_transaction_id <> '00000000-0000-0000-0000-000000000000'::uuid AND operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND original_transaction_points <> 0 AND required_points > 0 AND ((kind = 'EarnedClawback' AND original_transaction_points > 0) OR (kind = 'RedemptionRestoration' AND original_transaction_points < 0))"));
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(value => value.PlanFingerprint).HasMaxLength(64).IsRequired();
        builder.HasAlternateKey(value => new { value.SourceOrderId, value.Id });
        builder.HasIndex(value => new { value.AmendmentId, value.OriginalTransactionId, value.Kind }).IsUnique();
        builder.HasIndex(value => new { value.SourceOrderId, value.OriginalTransactionId, value.Kind });
        builder.HasOne<OrderAmendment>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.AmendmentId })
            .HasPrincipalKey(value => new { value.SourceOrderId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshot>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.SnapshotId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshotOwnerLink>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.OwnerLinkId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingAwardWitness>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.AwardWitnessId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderAmendmentResolutionOperation>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.OperationId })
            .HasPrincipalKey(value => new { value.SourceOrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
