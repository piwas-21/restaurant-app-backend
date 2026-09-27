using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OptionSetAppliedRowConfiguration : IEntityTypeConfiguration<OptionSetAppliedRow>
{
    public void Configure(EntityTypeBuilder<OptionSetAppliedRow> builder)
    {
        builder.ToTable("OptionSetAppliedRows");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.RowType).HasMaxLength(40).IsRequired();
        builder.Property(row => row.LastAppliedValuesJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne(row => row.Entry).WithMany().HasForeignKey(row => row.OptionSetEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.OptionSetAttachmentId, row.OptionSetEntryId }).IsUnique();
        builder.HasIndex(row => new { row.RowType, row.MaterializedRowId });
    }
}
