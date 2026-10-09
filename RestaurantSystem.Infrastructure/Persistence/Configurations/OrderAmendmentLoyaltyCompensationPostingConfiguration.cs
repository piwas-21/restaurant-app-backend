using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentLoyaltyCompensationPostingConfiguration
    : IEntityTypeConfiguration<OrderAmendmentLoyaltyCompensationPosting>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentLoyaltyCompensationPosting> builder)
    {
        builder.ToTable("order_amendment_loyalty_compensation_postings", table => table.HasCheckConstraint(
            "ck_order_amendment_loyalty_compensation_posting_delta", "movement_transaction_id <> '00000000-0000-0000-0000-000000000000'::uuid AND points_delta <> 0"));
        builder.HasIndex(value => value.CompensationId).IsUnique();
        builder.HasIndex(value => value.MovementTransactionId).IsUnique();
        builder.HasOne<OrderAmendmentLoyaltyCompensation>().WithMany()
            .HasForeignKey(value => value.CompensationId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
