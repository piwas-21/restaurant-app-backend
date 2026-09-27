using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OptionSetAttachmentConfiguration : IEntityTypeConfiguration<OptionSetAttachment>
{
    public void Configure(EntityTypeBuilder<OptionSetAttachment> builder)
    {
        builder.ToTable("OptionSetAttachments", table => table.HasCheckConstraint(
            "ck_option_set_attachments_target",
            $"(role = {(int)OptionSetAttachmentRole.BundleChoice} AND target_menu_section_id IS NOT NULL AND target_customization_group_id IS NULL) " +
            $"OR (role = {(int)OptionSetAttachmentRole.ProductChoice} AND target_menu_section_id IS NULL AND target_customization_group_id IS NOT NULL) " +
            $"OR (role NOT IN ({(int)OptionSetAttachmentRole.BundleChoice}, {(int)OptionSetAttachmentRole.ProductChoice}) " +
            "AND target_menu_section_id IS NULL AND target_customization_group_id IS NULL)"));
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Role).HasConversion<int>().IsRequired();
        builder.Property(attachment => attachment.Version).HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(attachment => attachment.IntentionalDifferenceReason)
            .HasMaxLength(OptionSetSchemaLimits.IntentionalDifferenceReasonLength);
        builder.Property(attachment => attachment.LastIdempotencyKey)
            .HasMaxLength(OptionSetSchemaLimits.IdempotencyKeyLength);
        builder.HasOne<Product>().WithMany().HasForeignKey(attachment => attachment.TargetProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MenuSection>().WithMany().HasForeignKey(attachment => attachment.TargetMenuSectionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductCustomizationGroup>().WithMany().HasForeignKey(attachment => attachment.TargetCustomizationGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(attachment => attachment.AppliedRows).WithOne(row => row.Attachment)
            .HasForeignKey(row => row.OptionSetAttachmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(attachment => new { attachment.TargetProductId, attachment.Role })
            .IsUnique().HasFilter("\"target_menu_section_id\" IS NULL AND \"target_customization_group_id\" IS NULL");
        builder.HasIndex(attachment => new { attachment.TargetMenuSectionId, attachment.Role })
            .IsUnique().HasFilter("\"target_menu_section_id\" IS NOT NULL");
        builder.HasIndex(attachment => new { attachment.TargetCustomizationGroupId, attachment.Role })
            .IsUnique().HasFilter("\"target_customization_group_id\" IS NOT NULL");
    }
}
