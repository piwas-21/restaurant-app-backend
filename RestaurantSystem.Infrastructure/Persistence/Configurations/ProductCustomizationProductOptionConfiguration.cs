using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductCustomizationProductOptionConfiguration : IEntityTypeConfiguration<ProductCustomizationProductOption>
{
    public void Configure(EntityTypeBuilder<ProductCustomizationProductOption> builder)
    {
        builder.Property(option => option.AdditionalPrice).HasPrecision(10, 2);

        builder.HasOne(option => option.ProductCustomizationGroup)
            .WithMany(group => group.ProductOptions)
            .HasForeignKey(option => option.ProductCustomizationGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(option => option.OptionProduct)
            .WithMany(product => product.CustomizationOptionMemberships)
            .HasForeignKey(option => option.OptionProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(option => new { option.ProductCustomizationGroupId, option.OptionProductId }).IsUnique();
        builder.HasIndex(option => new { option.ProductCustomizationGroupId, option.DisplayOrder });
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_product_customization_product_option_price",
            "additional_price >= 0"));
    }
}
