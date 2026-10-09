using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountCashRefundEvidenceConfiguration : IEntityTypeConfiguration<AccountCashRefundEvidence>
{
    public void Configure(EntityTypeBuilder<AccountCashRefundEvidence> builder)
    {
        builder.ToTable("account_cash_refund_evidence", table => table.HasCheckConstraint(
            "ck_account_cash_refund_evidence_shape",
            "exact_refund_amount_minor > 0 AND cash_returned_minor >= 0"
            + " AND refund_adjustment_minor = cash_returned_minor - exact_refund_amount_minor"
            + " AND currency ~ '^[A-Z]{3}$'"
            + " AND actor_id <> '00000000-0000-0000-0000-000000000000'"
            + " AND actor_role = 'Admin' AND length(till_reference) BETWEEN 1 AND 80"));
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.ActorRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.TillReference).HasMaxLength(80).IsRequired();
        builder.HasIndex(value => value.IntentId).IsUnique();
        builder.HasOne(value => value.Intent).WithOne(value => value.ReturnEvidence)
            .HasForeignKey<AccountCashRefundEvidence>(value => value.IntentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
