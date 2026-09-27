using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TranslationGenerationBatchConfiguration : IEntityTypeConfiguration<TranslationGenerationBatch>
{
    public void Configure(EntityTypeBuilder<TranslationGenerationBatch> builder)
    {
        builder.ToTable("translation_generation_batches");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.RequestedBy).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Provider).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Model).HasMaxLength(80).IsRequired();
        builder.Property(row => row.EstimatedCostUsd).HasPrecision(18, 6);
        builder.HasIndex(row => row.CreatedAt);
        builder.HasIndex(row => row.Fingerprint);
    }
}
