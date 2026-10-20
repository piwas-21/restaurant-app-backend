using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerOrderCheckoutJournalConfiguration : IEntityTypeConfiguration<CustomerOrderCheckoutJournal>
{
    public void Configure(EntityTypeBuilder<CustomerOrderCheckoutJournal> builder)
    {
        builder.ToTable("customer_order_checkout_journals", table => table.HasCheckConstraint(
            "ck_customer_order_checkout_journal_shape", "order_id <> '00000000-0000-0000-0000-000000000000'::uuid"
            + " AND operation_id <> '00000000-0000-0000-0000-000000000000'::uuid"
            + " AND amount_minor > 0 AND currency ~ '^[a-z]{3}$'"
            + " AND (application_fee_minor IS NULL OR (application_fee_minor > 0 AND application_fee_minor < amount_minor))"
            + " AND expires_at > started_at AND maximum_create_retry_at > started_at"
            + " AND maximum_create_retry_at <= started_at + INTERVAL '23 hours'"
            + " AND ((lease_id IS NULL AND lease_expires_at IS NULL) OR (lease_id IS NOT NULL AND lease_expires_at IS NOT NULL))"));
        builder.HasKey(value => value.OrderId);
        builder.Property(value => value.OrderId).ValueGeneratedNever();
        builder.Property(value => value.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.ProviderAccountId).HasMaxLength(255).IsRequired();
        builder.Property(value => value.CreateIdempotencyKey).HasMaxLength(255).IsRequired();
        builder.Property(value => value.CreatePayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.ReturnUrl).HasMaxLength(2048).IsRequired();
        builder.Property(value => value.ProviderSessionId).HasMaxLength(255);
        builder.HasIndex(value => value.OperationId).IsUnique();
        builder.HasIndex(value => value.CreateIdempotencyKey).IsUnique();
        builder.HasIndex(value => value.ProviderSessionId).IsUnique().HasFilter("provider_session_id IS NOT NULL");
        // No cascade/FK: order removal cannot release the only evidence of an unknown provider create.
    }
}
