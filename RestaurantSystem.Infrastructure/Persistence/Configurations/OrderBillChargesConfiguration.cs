using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillChargesConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.BillChargesJson).HasColumnType("text").HasDefaultValue("[]");
        builder.Property(o => o.BillChargeAmount).HasPrecision(18, 2).HasDefaultValue(0m);
    }
}
public sealed class RestaurantBillChargesConfiguration : IEntityTypeConfiguration<RestaurantInfo>
{
    public void Configure(EntityTypeBuilder<RestaurantInfo> builder) =>
        builder.Property(r => r.BillChargeRulesJson).HasColumnType("text").HasDefaultValue("[]");
}
