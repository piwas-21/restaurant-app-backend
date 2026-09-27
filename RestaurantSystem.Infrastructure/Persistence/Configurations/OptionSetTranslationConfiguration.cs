using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OptionSetTranslationConfiguration : IEntityTypeConfiguration<OptionSetTranslation>
{
    public void Configure(EntityTypeBuilder<OptionSetTranslation> builder)
    {
        builder.ToTable("OptionSetTranslations");
        builder.HasKey(translation => translation.Id);
        builder.Property(translation => translation.LanguageCode).HasMaxLength(OptionSetSchemaLimits.SourceLocaleLength)
            .IsRequired();
        builder.Property(translation => translation.Name).HasMaxLength(OptionSetSchemaLimits.TranslationNameLength)
            .IsRequired();
        builder.HasIndex(translation => new { translation.OptionSetId, translation.LanguageCode }).IsUnique();
    }
}
