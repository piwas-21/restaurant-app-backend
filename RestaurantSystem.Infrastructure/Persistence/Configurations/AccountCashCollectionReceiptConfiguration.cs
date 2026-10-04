using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountCashCollectionReceiptConfiguration
    : IEntityTypeConfiguration<AccountCashCollectionReceipt>
{
    public void Configure(EntityTypeBuilder<AccountCashCollectionReceipt> builder)
    {
        builder.ToTable("account_cash_collection_receipts", table => table.HasCheckConstraint(
            "ck_account_cash_collection_receipt_shape",
            "payment_method = 'Cash' AND exact_amount_minor > 0 AND due_amount_minor > 0"
            + " AND adjustment_minor = due_amount_minor - exact_amount_minor"
            + " AND adjustment_minor BETWEEN -2 AND 2"
            + " AND received_minor >= due_amount_minor AND change_minor = received_minor - due_amount_minor"
            + " AND expected_account_revision > 0 AND expected_version > 0"
            + " AND currency ~ '^[A-Z]{3}$' AND actor_id <> '00000000-0000-0000-0000-000000000000'"
            + " AND actor_kind = 'Staff' AND actor_role IN ('Admin','Cashier','Server')"
            + " AND request_hash ~ '^[a-f0-9]{64}$'"
            + " AND ((policy_version = 'chf-cash-5-rappen-v1' AND currency = 'CHF'"
            + " AND due_amount_minor % 5 = 0) OR (policy_version = 'exact-v1' AND currency <> 'CHF'"
            + " AND due_amount_minor = exact_amount_minor))"));
        builder.Property(value => value.PolicyVersion).HasMaxLength(40).IsRequired();
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.ActorKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.ActorRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.HasKey(value => value.Id).HasName("pk_account_cash_collection_receipt");
        builder.HasIndex(value => value.AttemptId)
            .HasDatabaseName("ix_account_cash_collection_receipt_attempt_id").IsUnique();
        builder.HasOne(value => value.Attempt).WithOne(value => value.CashCollectionReceipt)
            .HasForeignKey<AccountCashCollectionReceipt>(value => value.AttemptId)
            .HasConstraintName("fk_account_cash_collection_receipt_accountpaymentattempts_atte~")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
