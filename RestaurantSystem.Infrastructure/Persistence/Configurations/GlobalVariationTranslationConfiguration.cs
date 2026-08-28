using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class GlobalVariationTranslationConfiguration : IEntityTypeConfiguration<GlobalVariationTranslation>
{
    public void Configure(EntityTypeBuilder<GlobalVariationTranslation> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.LanguageCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(100);

        // One name per language per row. `ProductVariationDescription` shipped WITHOUT this and the
        // read path has carried a `g.First()` ever since to pick a winner from rows that should
        // never both exist (backend analysis §5); the new table starts with the constraint instead
        // of the guard.
        builder.HasIndex(t => new { t.GlobalVariationId, t.LanguageCode })
            .IsUnique();
    }
}
