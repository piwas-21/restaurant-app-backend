using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class RestaurantPhoneNumberConfiguration : IEntityTypeConfiguration<RestaurantPhoneNumber>
{
    public void Configure(EntityTypeBuilder<RestaurantPhoneNumber> builder)
    {
        builder.ToTable("RestaurantPhoneNumbers");

        builder.Property(p => p.Label).HasMaxLength(50);

        // E.164 max length is 15 digits + optional `+`. Cap at 20 for safety.
        builder.Property(p => p.Number).IsRequired().HasMaxLength(20);

        builder.HasIndex(p => p.RestaurantInfoId);
    }
}
