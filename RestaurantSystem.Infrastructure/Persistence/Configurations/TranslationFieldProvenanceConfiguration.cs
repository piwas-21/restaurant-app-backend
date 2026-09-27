using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TranslationFieldProvenanceConfiguration : IEntityTypeConfiguration<TranslationFieldProvenance>
{
    public void Configure(EntityTypeBuilder<TranslationFieldProvenance> builder)
    {
        builder.ToTable("translation_field_provenances");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.EntityType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.FieldKey).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Locale).HasMaxLength(10).IsRequired();
        builder.Property(row => row.SourceLocale).HasMaxLength(10).IsRequired();
        builder.Property(row => row.SourceHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.TextHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Kind).HasMaxLength(24).IsRequired();
        builder.Property(row => row.ReviewStatus).HasMaxLength(16).IsRequired();
        builder.Property(row => row.TemplateId).HasMaxLength(120);
        builder.Property(row => row.ReviewerId).HasMaxLength(128);
        builder.HasIndex(row => new { row.EntityType, row.EntityId, row.FieldKey, row.Locale }).IsUnique();
    }
}
