using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentLoyaltyOwnerHoldConfiguration
    : IEntityTypeConfiguration<OrderAmendmentLoyaltyOwnerHold>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentLoyaltyOwnerHold> builder)
    {
        builder.ToTable("order_amendment_loyalty_owner_holds");
        builder.HasIndex(value => new { value.SourceOrderId, value.OperationId, value.OwnerLinkId }).IsUnique();
        builder.HasIndex(value => new { value.SourceOrderId, value.OwnerLinkId })
            .IsUnique().HasFilter("released_at IS NULL");
        builder.HasIndex(value => new { value.OwnerLinkId, value.ReleasedAt });
        builder.HasOne<OrderAmendmentResolutionOperation>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.OperationId })
            .HasPrincipalKey(value => new { value.SourceOrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderBillingSnapshotOwnerLink>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.OwnerLinkId })
            .HasPrincipalKey(value => new { value.OrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
