using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CatalogueImportSessionTemplateConfiguration : IEntityTypeConfiguration<CatalogueImportSessionTemplate>
{
    public void Configure(EntityTypeBuilder<CatalogueImportSessionTemplate> builder)
    {
        builder.Property(x => x.TemplateId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RevisionJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DecisionJson).HasColumnType("jsonb");
        builder.Property(x => x.SelectionRole).HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LocalEntityType).HasMaxLength(32);
        builder.Property(x => x.FailureCode).HasMaxLength(64);
        builder.HasIndex(x => new { x.SessionId, x.TemplateId, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.SessionId, x.IsSelected });
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_catalogue_import_session_templates_revision", "revision > 0"));
    }
}
