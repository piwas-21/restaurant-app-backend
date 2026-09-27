using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TranslationSuggestionConfiguration : IEntityTypeConfiguration<TranslationSuggestion>
{
    public void Configure(EntityTypeBuilder<TranslationSuggestion> builder)
    {
        builder.ToTable("translation_suggestions");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.BatchId);
        builder.Property(row => row.EntityType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ClientKey).HasMaxLength(128);
        builder.Property(row => row.FieldKey).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Locale).HasMaxLength(10).IsRequired();
        builder.Property(row => row.SourceLocale).HasMaxLength(10).IsRequired();
        builder.Property(row => row.SourceHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ContextHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SuggestedText).HasMaxLength(1000).IsRequired();
        builder.Property(row => row.ReviewedText).HasMaxLength(1000);
        builder.Property(row => row.Provider).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Model).HasMaxLength(80).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(16).IsRequired();
        builder.Property(row => row.RequestedBy).HasMaxLength(128).IsRequired();
        builder.Property(row => row.ReviewerId).HasMaxLength(128);
        builder.HasIndex(row => new
        {
            row.Fingerprint,
            row.EntityType,
            row.EntityId,
            row.ClientKey,
            row.FieldKey,
            row.Locale,
            row.RequestedBy
        });
        builder.HasIndex(row => new { row.CreatedAt, row.RequestedBy });
    }
}
