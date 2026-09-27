using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OptionSetAuthoringRevisionConfiguration : IEntityTypeConfiguration<OptionSetAuthoringRevision>
{
    public void Configure(EntityTypeBuilder<OptionSetAuthoringRevision> builder)
    {
        builder.ToTable("OptionSetAuthoringRevisions");
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.SummaryJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<ProductCustomizationGroup>().WithMany().HasForeignKey(revision => revision.TargetCustomizationGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(revision => new { revision.TargetProductId, revision.CreatedAt });
        builder.HasIndex(revision => new { revision.OptionSetId, revision.CreatedAt });
    }
}
