using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentRefundLegConfiguration : IEntityTypeConfiguration<OrderAmendmentRefundLeg>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentRefundLeg> builder)
    {
        builder.ToTable("order_amendment_refund_legs", table => table.HasCheckConstraint(
            "ck_amendment_refund_leg_amount", "amount_minor > 0"));
        builder.Property(value => value.Custody).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.FrozenScopesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(value => value.ProviderAccountId).HasMaxLength(255);
        builder.Property(value => value.ProviderChargeId).HasMaxLength(255);
        builder.Property(value => value.ProviderIntentId).HasMaxLength(255);
        builder.Property(value => value.ManualTillReference).HasMaxLength(80);
        builder.Property(value => value.FailureCode).HasMaxLength(80);
        builder.HasIndex(value => new { value.OperationId, value.SourcePaymentId }).IsUnique();
        builder.HasIndex(value => new { value.ProviderAccountId, value.ProviderChargeId, value.State });
        builder.HasOne<OrderAmendmentResolutionOperation>().WithMany(value => value.Legs)
            .HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderPayment>().WithMany().HasForeignKey(value => value.SourcePaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountPaymentAttempt>().WithMany().HasForeignKey(value => value.AccountPaymentAttemptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
