using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentIdentityConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasIndex(order => order.AmendmentRootOrderId);
        builder.HasOne<Order>().WithMany().HasForeignKey(order => order.AmendmentRootOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
