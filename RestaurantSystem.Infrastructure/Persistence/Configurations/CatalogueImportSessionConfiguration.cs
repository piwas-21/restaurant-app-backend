using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CatalogueImportSessionConfiguration : IEntityTypeConfiguration<CatalogueImportSession>
{
    public void Configure(EntityTypeBuilder<CatalogueImportSession> builder)
    {
        builder.Property(x => x.RootTemplateId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Locale).HasMaxLength(16).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreateSelectionJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.LastImportIdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.LastImportExpectedVersion);
        builder.Property(x => x.LastImportResultJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.RootTemplateId, x.RootRevision });
        builder.HasMany(x => x.Templates).WithOne(x => x.Session)
            .HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_catalogue_import_sessions_root_revision", "root_revision > 0"));
    }
}
