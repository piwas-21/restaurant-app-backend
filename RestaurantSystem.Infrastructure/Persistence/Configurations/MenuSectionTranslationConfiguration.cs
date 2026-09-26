using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class MenuSectionTranslationConfiguration : IEntityTypeConfiguration<MenuSectionTranslation>
{
    public void Configure(EntityTypeBuilder<MenuSectionTranslation> builder)
    {
        builder.ToTable("MenuSectionTranslations");
        builder.HasKey(translation => translation.Id);
        builder.Property(translation => translation.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(translation => translation.Name).IsRequired().HasMaxLength(100);
        builder.Property(translation => translation.Description).HasMaxLength(500);
        builder.HasIndex(translation => new { translation.MenuSectionId, translation.LanguageCode }).IsUnique();
        builder.HasOne(translation => translation.MenuSection)
            .WithMany(section => section.Translations)
            .HasForeignKey(translation => translation.MenuSectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
