using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentRefundAttemptConfiguration : IEntityTypeConfiguration<OrderAmendmentRefundAttempt>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentRefundAttempt> builder)
    {
        builder.ToTable("order_amendment_refund_attempts", table => table.HasCheckConstraint(
            "ck_amendment_refund_attempt_sequence", "sequence > 0"));
        builder.Property(value => value.IdempotencyKey).HasMaxLength(255).IsRequired();
        builder.HasIndex(value => new { value.RefundLegId, value.Sequence }).IsUnique();
        builder.HasIndex(value => value.IdempotencyKey).IsUnique();
        builder.HasOne<OrderAmendmentRefundLeg>().WithMany(value => value.Attempts)
            .HasForeignKey(value => value.RefundLegId).OnDelete(DeleteBehavior.Restrict);
    }
}
