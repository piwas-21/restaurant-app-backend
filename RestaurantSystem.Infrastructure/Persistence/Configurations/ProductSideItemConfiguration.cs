using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductSideItemConfiguration : IEntityTypeConfiguration<ProductSideItem>
{
    public void Configure(EntityTypeBuilder<ProductSideItem> builder)
    {
        builder.ToTable("ProductSideItems");

        builder.HasKey(psi => psi.Id);

        // Main Product relationship
        builder.HasOne(psi => psi.MainProduct)
            .WithMany(p => p.SuggestedSideItems)
            .HasForeignKey(psi => psi.MainProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(psi => psi.DisplayOrder);
    }
}
