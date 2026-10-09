using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentLoyaltyReservationConfiguration
    : IEntityTypeConfiguration<OrderAmendmentLoyaltyReservation>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentLoyaltyReservation> builder)
    {
        builder.ToTable("order_amendment_loyalty_reservations", table => table.HasCheckConstraint(
            "ck_order_amendment_loyalty_reservation_state",
            "state IN ('HeldShortfall', 'Reserved', 'Consumed', 'Released')"));
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(24);
        builder.HasIndex(value => value.CompensationId).IsUnique();
        builder.HasIndex(value => value.OperationId);
        builder.HasIndex(value => new { value.OwnerLinkId, value.State });
        builder.HasOne<OrderAmendmentLoyaltyCompensation>().WithMany()
            .HasForeignKey(value => new { value.SourceOrderId, value.CompensationId })
            .HasPrincipalKey(value => new { value.SourceOrderId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
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
