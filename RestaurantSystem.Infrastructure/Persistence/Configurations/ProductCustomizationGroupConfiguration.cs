using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductCustomizationGroupConfiguration : IEntityTypeConfiguration<ProductCustomizationGroup>
{
    public void Configure(EntityTypeBuilder<ProductCustomizationGroup> builder)
    {
        builder.Property(group => group.Name).IsRequired().HasMaxLength(100);
        builder.Property(group => group.Description).HasMaxLength(500);

        builder.HasOne(group => group.Product)
            .WithMany(product => product.CustomizationGroups)
            .HasForeignKey(group => group.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Display order is presentation metadata, not identity. Keeping it non-unique lets a
        // full-replace writer swap two rows without a transient unique-key collision.
        builder.HasIndex(group => new { group.ProductId, group.DisplayOrder });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_product_customization_group_min", "min_selection >= 0");
            table.HasCheckConstraint("ck_product_customization_group_max", "max_selection >= min_selection");
            table.HasCheckConstraint("ck_product_customization_group_free", "included_free_units >= 0");
        });
    }
}
