using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class RestaurantInfoConfiguration : IEntityTypeConfiguration<RestaurantInfo>
{
    public void Configure(EntityTypeBuilder<RestaurantInfo> builder)
    {
        builder.ToTable("RestaurantInfo");

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.AddressLine1).IsRequired().HasMaxLength(200);
        builder.Property(r => r.AddressLine2).HasMaxLength(200);
        builder.Property(r => r.City).IsRequired().HasMaxLength(100);
        builder.Property(r => r.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(r => r.Country).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Latitude).HasColumnType("decimal(9,6)");
        builder.Property(r => r.Longitude).HasColumnType("decimal(9,6)");
        builder.Property(r => r.Email).IsRequired().HasMaxLength(254);
        builder.Property(r => r.Website).HasMaxLength(2048);

        builder.HasMany(r => r.PhoneNumbers)
            .WithOne(p => p.RestaurantInfo)
            .HasForeignKey(p => p.RestaurantInfoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
