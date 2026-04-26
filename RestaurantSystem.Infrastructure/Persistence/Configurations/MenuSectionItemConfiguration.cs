using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class MenuSectionItemConfiguration : IEntityTypeConfiguration<MenuSectionItem>
{
    public void Configure(EntityTypeBuilder<MenuSectionItem> builder)
    {
        builder.HasOne(i => i.MenuSection)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.MenuSectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict); // Don't delete item if product is deleted (or maybe SetNull? Restrict is safer)

        builder.Property(i => i.AdditionalPrice)
            .HasColumnType("decimal(18,2)");
    }
}
