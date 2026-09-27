using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CatalogueTemplateAdoptionConfiguration : IEntityTypeConfiguration<CatalogueTemplateAdoption>
{
    public void Configure(EntityTypeBuilder<CatalogueTemplateAdoption> builder)
    {
        builder.Property(x => x.SourceTemplateId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.SourceEntryId).HasMaxLength(160);
        builder.Property(x => x.LocalEntityType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.BaselineFieldsJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.AdoptionId, x.SourceTemplateId, x.SourceRevision, x.LocalEntityType })
            .IsUnique().HasFilter("\"source_entry_id\" IS NULL");
        builder.HasIndex(x => new
        { x.AdoptionId, x.SourceTemplateId, x.SourceRevision, x.LocalEntityType, x.SourceEntryId })
            .IsUnique().HasFilter("\"source_entry_id\" IS NOT NULL");
        builder.HasIndex(x => new { x.SourceTemplateId, x.SourceRevision, x.LocalEntityType })
            .IsUnique().HasFilter("\"is_default\" = true AND \"source_entry_id\" IS NULL");
        builder.HasIndex(x => new { x.SourceTemplateId, x.SourceRevision, x.LocalEntityType, x.SourceEntryId })
            .IsUnique().HasFilter("\"is_default\" = true AND \"source_entry_id\" IS NOT NULL");
        builder.HasIndex(x => new { x.SessionId, x.AdoptionId });
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_catalogue_template_adoptions_source_revision", "source_revision > 0"));
    }
}
