using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountCashRefundIntentConfiguration : IEntityTypeConfiguration<AccountCashRefundIntent>
{
    public void Configure(EntityTypeBuilder<AccountCashRefundIntent> builder)
    {
        builder.ToTable("account_cash_refund_intents", table => table.HasCheckConstraint(
            "ck_account_cash_refund_intent_shape",
            "original_exact_amount_minor > 0 AND original_due_amount_minor > 0"
            + " AND original_adjustment_minor = original_due_amount_minor - original_exact_amount_minor"
            + " AND original_adjustment_minor BETWEEN -2 AND 2"
            + " AND previously_refunded_exact_minor >= 0 AND previously_refunded_cash_minor >= 0"
            + " AND exact_refund_amount_minor > 0 AND cash_refund_amount_minor >= 0"
            + " AND refund_adjustment_minor = cash_refund_amount_minor - exact_refund_amount_minor"
            + " AND retained_exact_amount_minor >= 0 AND retained_cash_due_minor >= 0"
            + " AND original_exact_amount_minor = previously_refunded_exact_minor"
            + " + exact_refund_amount_minor + retained_exact_amount_minor"
            + " AND original_due_amount_minor = previously_refunded_cash_minor"
            + " + cash_refund_amount_minor + retained_cash_due_minor"
            + " AND currency ~ '^[A-Z]{3}$' AND prior_history_fingerprint ~ '^[a-f0-9]{64}$'"
            + " AND ((policy_version = 'chf-cash-5-rappen-v1' AND currency = 'CHF')"
            + " OR (policy_version = 'exact-v1' AND currency <> 'CHF'"
            + " AND original_due_amount_minor = original_exact_amount_minor))"));
        builder.Property(value => value.PolicyVersion).HasMaxLength(40).IsRequired();
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.PriorHistoryFingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(value => value.RefundLegId).IsUnique();
        builder.HasIndex(value => value.AttemptId);
        builder.HasIndex(value => value.CollectionReceiptId);
        builder.HasIndex(value => value.OperationId);
        builder.HasOne(value => value.RefundLeg).WithOne(value => value.CashRefundIntent)
            .HasForeignKey<AccountCashRefundIntent>(value => value.RefundLegId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Operation).WithMany().HasForeignKey(value => value.OperationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Attempt).WithMany().HasForeignKey(value => value.AttemptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.CollectionReceipt).WithMany().HasForeignKey(value => value.CollectionReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
