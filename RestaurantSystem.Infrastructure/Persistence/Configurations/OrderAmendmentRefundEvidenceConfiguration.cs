using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentRefundEvidenceConfiguration : IEntityTypeConfiguration<OrderAmendmentRefundEvidence>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentRefundEvidence> builder)
    {
        builder.ToTable("order_amendment_refund_evidence", table => table.HasCheckConstraint(
            "ck_amendment_refund_evidence_amount", "amount_minor >= 0 AND sequence > 0"));
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.ActorRole).HasMaxLength(30).IsRequired();
        builder.Property(value => value.TillReference).HasMaxLength(80);
        builder.Property(value => value.ProviderRefundId).HasMaxLength(255);
        builder.Property(value => value.ProviderRefundStatus).HasMaxLength(40);
        builder.Property(value => value.ProviderChargeId).HasMaxLength(255);
        builder.Property(value => value.ProviderIntentId).HasMaxLength(255);
        builder.Property(value => value.ProviderAccountId).HasMaxLength(255);
        builder.Property(value => value.FailureCode).HasMaxLength(80);
        builder.Property(value => value.EvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(value => new { value.RefundLegId, value.Sequence }).IsUnique();
        builder.HasIndex(value => value.ProviderRefundId).IsUnique()
            .HasFilter("provider_refund_id IS NOT NULL AND state = 'Succeeded'");
        builder.HasOne<OrderAmendmentRefundLeg>().WithMany().HasForeignKey(value => value.RefundLegId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderAmendmentRefundAttempt>().WithMany().HasForeignKey(value => value.RefundAttemptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
