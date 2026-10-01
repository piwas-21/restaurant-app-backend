using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderExternalConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // One optional reference adds no collection cross-product. Synchronous staff and printer
        // projections must carry the same source as the explicit asynchronous projection.
        builder.Navigation(order => order.ExternalReference).AutoInclude();
    }
}
