using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductCustomizationGroupDescriptionConfiguration : IEntityTypeConfiguration<ProductCustomizationGroupDescription>
{
    public void Configure(EntityTypeBuilder<ProductCustomizationGroupDescription> builder)
    {
        builder.Property(description => description.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(description => description.Name).IsRequired().HasMaxLength(100);
        builder.Property(description => description.Description).HasMaxLength(500);

        builder.HasOne(description => description.ProductCustomizationGroup)
            .WithMany(group => group.Descriptions)
            .HasForeignKey(description => description.ProductCustomizationGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(description => new
        {
            description.ProductCustomizationGroupId,
            description.LanguageCode
        }).IsUnique();
    }
}
