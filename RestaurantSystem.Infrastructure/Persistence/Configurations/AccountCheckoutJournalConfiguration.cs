using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountCheckoutJournalConfiguration : IEntityTypeConfiguration<AccountCheckoutJournal>
{
    public void Configure(EntityTypeBuilder<AccountCheckoutJournal> builder)
    {
        builder.ToTable("account_checkout_journals", table => table.HasCheckConstraint(
            "ck_account_checkout_journal_shape", "amount_minor > 0 AND started_attempt_version > 0"
            + " AND provider_captured_minor >= 0 AND provider_captured_minor <= amount_minor"
            + " AND provider_refunded_minor >= 0 AND provider_refunded_minor <= provider_captured_minor"
            + " AND expires_at > started_at AND maximum_create_retry_at > started_at"
            + " AND maximum_create_retry_at <= started_at + INTERVAL '23 hours'"
            + " AND ((lease_id IS NULL AND lease_expires_at IS NULL)"
            + " OR (lease_id IS NOT NULL AND lease_expires_at IS NOT NULL))"
            + " AND currency ~ '^[A-Z]{3}$' AND reconcile_failure_count >= 0"
            + " AND ((receipt_credential_hash IS NULL AND receipt_expires_at IS NULL)"
            + " OR (receipt_credential_hash IS NOT NULL AND receipt_expires_at IS NOT NULL))"));
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.ProviderAccountId).HasMaxLength(255).IsRequired();
        builder.Property(value => value.CreateIdempotencyKey).HasMaxLength(255).IsRequired();
        builder.Property(value => value.CreatePayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.ReturnBaseUrl).HasMaxLength(2048).IsRequired();
        builder.Property(value => value.ProviderSessionId).HasMaxLength(255);
        builder.Property(value => value.ProviderIntentId).HasMaxLength(255);
        builder.Property(value => value.ProviderChargeId).HasMaxLength(255);
        builder.Property(value => value.ReceiptCredentialHash).HasMaxLength(64);
        builder.Property(value => value.LastFailureCode).HasMaxLength(80);
        builder.Property(value => value.WebhookWakeupPending).HasDefaultValue(false).IsRequired();
        builder.HasIndex(value => value.AttemptId).IsUnique();
        builder.HasIndex(value => value.CreateIdempotencyKey).IsUnique();
        builder.HasIndex(value => value.ProviderSessionId).IsUnique().HasFilter("provider_session_id IS NOT NULL");
        builder.HasIndex(value => value.ProviderIntentId).IsUnique().HasFilter("provider_intent_id IS NOT NULL");
        builder.HasIndex(value => value.ProviderChargeId).IsUnique().HasFilter("provider_charge_id IS NOT NULL");
        builder.HasIndex(value => new { value.NextReconcileAt, value.LeaseExpiresAt });
        builder.HasOne(value => value.Attempt).WithOne().HasForeignKey<AccountCheckoutJournal>(value => value.AttemptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
