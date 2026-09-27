using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CatalogueMatchDecisionConfiguration : IEntityTypeConfiguration<CatalogueMatchDecision>
{
    public void Configure(EntityTypeBuilder<CatalogueMatchDecision> builder)
    {
        builder.Property(x => x.SourceTemplateId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.CandidateType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.SourceTemplateId, x.SourceRevision, x.CandidateType, x.CandidateId })
            .IsUnique();
        builder.HasIndex(x => new { x.NormalizedName, x.CandidateType });
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_catalogue_match_decisions_source_revision", "source_revision > 0"));
    }
}
