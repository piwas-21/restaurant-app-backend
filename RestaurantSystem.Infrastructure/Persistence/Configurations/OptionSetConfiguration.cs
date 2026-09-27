using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OptionSetConfiguration : IEntityTypeConfiguration<OptionSet>
{
    public void Configure(EntityTypeBuilder<OptionSet> builder)
    {
        builder.ToTable("OptionSets");
        builder.HasKey(set => set.Id);
        builder.Property(set => set.Name).HasMaxLength(OptionSetSchemaLimits.OptionSetNameLength).IsRequired();
        builder.Property(set => set.SourceLocale).HasMaxLength(OptionSetSchemaLimits.SourceLocaleLength)
            .HasDefaultValue("en").IsRequired();
        builder.Property(set => set.NormalizedName).HasMaxLength(OptionSetSchemaLimits.NormalizedNameLength).IsRequired();
        builder.Property(set => set.Kind).HasConversion<int>().IsRequired();
        builder.Property(set => set.Status).HasConversion<int>().IsRequired();
        builder.Property(set => set.Version).HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(set => set.SourceTemplateId).HasMaxLength(OptionSetSchemaLimits.SourceIdentifierLength);
        builder.Property(set => set.SourceOptionSetId).HasMaxLength(OptionSetSchemaLimits.SourceIdentifierLength);
        builder.HasIndex(set => new { set.Kind, set.NormalizedName }).IsUnique();
        builder.HasIndex(set => new { set.SourceTemplateId, set.SourceRevision, set.SourceOptionSetId })
            .IsUnique()
            .HasFilter("\"source_template_id\" IS NOT NULL AND \"source_revision\" IS NOT NULL AND \"source_option_set_id\" IS NOT NULL");
        builder.HasMany(set => set.Entries).WithOne(entry => entry.OptionSet)
            .HasForeignKey(entry => entry.OptionSetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(set => set.Attachments).WithOne(attachment => attachment.OptionSet)
            .HasForeignKey(attachment => attachment.OptionSetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(set => set.Translations).WithOne(translation => translation.OptionSet)
            .HasForeignKey(translation => translation.OptionSetId).OnDelete(DeleteBehavior.Cascade);
    }
}
