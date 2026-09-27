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
        builder.Property(translation => translation.LanguageCode).HasMaxLength(10).IsRequired();
        builder.Property(translation => translation.Name).HasMaxLength(120).IsRequired();
        builder.HasIndex(translation => new { translation.OptionSetId, translation.LanguageCode }).IsUnique();
    }
}
