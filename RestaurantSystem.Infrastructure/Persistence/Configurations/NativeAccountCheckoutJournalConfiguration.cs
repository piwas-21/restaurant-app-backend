using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class NativeAccountCheckoutJournalConfiguration : IEntityTypeConfiguration<AccountCheckoutJournal>
{
    public void Configure(EntityTypeBuilder<AccountCheckoutJournal> builder)
    {
        builder.ToTable("account_checkout_journals", table => table.HasCheckConstraint(
            "ck_account_checkout_native_scope",
            "(native_operation_id IS NULL AND native_tenant_id IS NULL) OR "
            + "(native_operation_id IS NOT NULL AND native_tenant_id IS NOT NULL "
            + "AND native_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid "
            + "AND native_tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        builder.HasIndex(row => row.NativeOperationId).IsUnique().HasFilter("native_operation_id IS NOT NULL");
        builder.HasOne<CustomerTablePaymentOperation>().WithMany().HasForeignKey(row => row.NativeOperationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
