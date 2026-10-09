using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountPaymentAttemptConfiguration : IEntityTypeConfiguration<AccountPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<AccountPaymentAttempt> builder)
    {
        builder.ToTable("account_payment_attempts", table => table.HasCheckConstraint(
            "ck_account_payment_attempt_shape", "amount_minor > 0 AND tip_minor >= 0 AND expected_account_revision > 0 AND version > 0"
            + " AND (equal_share_ordinal IS NULL OR equal_share_ordinal > 0)"
            + " AND ((mode IN ('Equal', 'CustomAmount') AND equal_share_plan_id IS NOT NULL AND equal_share_ordinal IS NOT NULL)"
            + " OR (mode NOT IN ('Equal', 'CustomAmount') AND equal_share_plan_id IS NULL AND equal_share_ordinal IS NULL))"));
        builder.Property(value => value.ActorKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(value => value.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.Version).IsConcurrencyToken();
        builder.Property(value => value.TipMinor).IsRequired();
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(value => value.ProviderSessionId).HasMaxLength(255);
        builder.Property(value => value.ProviderChargeId).HasMaxLength(255);
        builder.Property(value => value.ProviderAccountId).HasMaxLength(255);
        builder.Property(value => value.FailureCode).HasMaxLength(80);
        builder.HasIndex(value => value.OperationId).IsUnique();
        builder.HasIndex(value => new { value.ServiceSessionId, value.State });
        builder.HasIndex(value => value.ProviderSessionId).IsUnique().HasFilter("provider_session_id IS NOT NULL");
        builder.HasIndex(value => value.ProviderChargeId).IsUnique().HasFilter("provider_charge_id IS NOT NULL");
        builder.HasIndex(value => new { value.EqualSharePlanId, value.EqualShareOrdinal });
        builder.HasOne(value => value.ServiceSession).WithMany().HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.EqualSharePlan).WithMany().HasForeignKey(value => value.EqualSharePlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
