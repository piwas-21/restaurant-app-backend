using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductVariationDescriptionConfiguration : IEntityTypeConfiguration<ProductVariationDescription>
{
    public void Configure(EntityTypeBuilder<ProductVariationDescription> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LanguageCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.HasOne(e => e.ProductVariation)
            .WithMany(v => v.Descriptions)
            .HasForeignKey(e => e.ProductVariationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
