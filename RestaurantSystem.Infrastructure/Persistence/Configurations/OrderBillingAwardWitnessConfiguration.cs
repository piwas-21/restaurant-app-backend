using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingAwardWitnessConfiguration : IEntityTypeConfiguration<OrderBillingAwardWitness>
{
    public void Configure(EntityTypeBuilder<OrderBillingAwardWitness> builder)
    {
        builder.ToTable("order_billing_award_witnesses", table => table.HasCheckConstraint(
            "ck_order_billing_award_witness_values", OrderBillingAwardJournalConstraintSql.Witness));
        builder.Property(value => value.Outcome).HasConversion<string>().HasMaxLength(24);
        builder.HasAlternateKey(value => new { value.OrderId, value.Id });
        builder.HasIndex(value => value.OrderId).IsUnique();
        builder.HasIndex(value => value.EarnedTransactionId).IsUnique()
            .HasFilter("\"earned_transaction_id\" IS NOT NULL");
        builder.HasOne<OrderBillingSnapshot>().WithMany()
            .HasForeignKey(value => value.OrderId).HasPrincipalKey(value => value.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshotOwnerLink>().WithMany()
            .HasForeignKey(value => new { value.OrderId, value.OwnerLinkId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
