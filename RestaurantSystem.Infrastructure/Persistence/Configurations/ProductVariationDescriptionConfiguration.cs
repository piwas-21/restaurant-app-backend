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

        // One description per language per variation — the constraint its ingredient twin has had
        // since it was written (ProductIngredientDescriptionConfiguration) and this table never got
        // (backend analysis §9 defect 2). Its absence is why ProductDtoMapper reads the language map
        // through a `g.First()`: two `en` rows for one variation were storable, and which one won
        // was whatever the database returned first. That call is now a formality rather than a
        // load-bearing guard.
        builder.HasIndex(e => new { e.ProductVariationId, e.LanguageCode })
            .IsUnique();
    }
}
