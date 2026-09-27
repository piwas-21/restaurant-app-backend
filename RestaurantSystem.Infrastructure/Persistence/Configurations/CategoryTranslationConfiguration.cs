using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> builder)
    {
        builder.ToTable("CategoryTranslations");
        builder.HasKey(translation => translation.Id);
        builder.Property(translation => translation.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(translation => translation.Name).IsRequired().HasMaxLength(200);
        builder.Property(translation => translation.Description).HasMaxLength(1000);
        builder.HasIndex(translation => new { translation.CategoryId, translation.LanguageCode }).IsUnique();
        builder.HasOne(translation => translation.Category)
            .WithMany(category => category.Translations)
            .HasForeignKey(translation => translation.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
