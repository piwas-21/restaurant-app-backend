using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class CustomerFormFieldConfigurationConfiguration : IEntityTypeConfiguration<CustomerFormFieldConfiguration>
{
    public void Configure(EntityTypeBuilder<CustomerFormFieldConfiguration> builder)
    {
        builder.ToTable("CustomerFormFieldConfigurations");

        builder.Property(c => c.FormKey).IsRequired().HasMaxLength(50);
        builder.Property(c => c.FieldKey).IsRequired().HasMaxLength(50);

        // One configuration row per (form, field) pair — the natural upsert key.
        builder.HasIndex(c => new { c.FormKey, c.FieldKey }).IsUnique();
    }
}
