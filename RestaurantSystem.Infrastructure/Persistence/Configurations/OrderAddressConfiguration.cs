using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderAddressConfiguration : IEntityTypeConfiguration<OrderAddress>
{
    public void Configure(EntityTypeBuilder<OrderAddress> builder)
    {
        builder.ToTable("OrderAddresses");

        builder.HasKey(x => x.Id);

        // One-to-one relationship with Order
        builder.HasOne(x => x.Order)
            .WithOne(o => o.DeliveryAddress)
            .HasForeignKey<OrderAddress>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optional relationship with UserAddress
        builder.HasOne(x => x.UserAddress)
            .WithMany()
            .HasForeignKey(x => x.UserAddressId)
            .OnDelete(DeleteBehavior.SetNull);

        // Property configurations
        builder.Property(x => x.Label)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.AddressLine1)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.AddressLine2)
            .HasMaxLength(200);

        builder.Property(x => x.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.State)
            .HasMaxLength(100);

        builder.Property(x => x.PostalCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.Country)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Phone)
            .HasMaxLength(20);

        builder.Property(x => x.DeliveryInstructions)
            .HasMaxLength(500);

        // Indexes
        builder.HasIndex(x => x.OrderId)
            .IsUnique();

        builder.HasIndex(x => x.UserAddressId);
    }
}
