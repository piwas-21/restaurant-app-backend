using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OptionSetEntryConfiguration : IEntityTypeConfiguration<OptionSetEntry>
{
    public void Configure(EntityTypeBuilder<OptionSetEntry> builder)
    {
        builder.ToTable("OptionSetEntries", table =>
        {
            table.HasCheckConstraint(
                "ck_option_set_entries_one_reference",
                "(global_ingredient_id IS NULL) <> (product_id IS NULL) AND (product_variation_id IS NULL OR product_id IS NOT NULL)");
            table.HasCheckConstraint("ck_option_set_entries_positive_quantity", "max_quantity >= 1");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Name).HasMaxLength(OptionSetSchemaLimits.EntryNameLength).IsRequired();
        builder.Property(entry => entry.SourceEntryId).HasMaxLength(OptionSetSchemaLimits.SourceIdentifierLength);
        builder.Property(entry => entry.Price).HasColumnType("decimal(18,2)");
        builder.Property(entry => entry.AdditionalPrice).HasColumnType("decimal(18,2)");
        builder.HasOne<GlobalIngredient>().WithMany().HasForeignKey(entry => entry.GlobalIngredientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(entry => entry.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariation>().WithMany().HasForeignKey(entry => entry.ProductVariationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.OptionSetId, entry.GlobalIngredientId })
            .IsUnique().HasFilter("\"global_ingredient_id\" IS NOT NULL");
        builder.HasIndex(entry => new { entry.OptionSetId, entry.ProductId })
            .IsUnique().HasFilter("\"product_id\" IS NOT NULL AND \"product_variation_id\" IS NULL");
        builder.HasIndex(entry => new { entry.OptionSetId, entry.ProductVariationId })
            .IsUnique().HasFilter("\"product_variation_id\" IS NOT NULL");
    }
}
