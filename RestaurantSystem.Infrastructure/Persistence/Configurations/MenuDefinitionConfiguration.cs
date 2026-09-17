using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class MenuDefinitionConfiguration : IEntityTypeConfiguration<MenuDefinition>
{
    public void Configure(EntityTypeBuilder<MenuDefinition> builder)
    {
        builder.HasOne(m => m.Product)
            .WithOne(p => p.MenuDefinition)
            .HasForeignKey<MenuDefinition>(m => m.ProductId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_menu_definitions_products_product_id");

        builder.HasIndex(m => m.ProductId)
            .IsUnique()
            .HasDatabaseName("ix_menu_definitions_product_id");

        builder.HasOne(m => m.ParentOfferProduct)
            .WithMany(p => p.MenuAlternatives)
            .HasForeignKey(m => m.ParentOfferProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_menu_definitions_products_parent_offer_product_id");

        builder.HasOne(m => m.ParentOfferVariation)
            .WithMany(v => v.MenuAlternatives)
            .HasForeignKey(m => m.ParentOfferVariationId)
            .OnDelete(DeleteBehavior.Restrict);

        // A parent may have one generic menu alternative and one alternative per variation. The
        // filtered indexes make PostgreSQL's NULL semantics explicit: a normal composite unique
        // index would incorrectly allow unlimited parent-wide (NULL variation) alternatives.
        builder.HasIndex(m => m.ParentOfferProductId)
            .IsUnique()
            .HasFilter("\"parent_offer_product_id\" IS NOT NULL AND \"parent_offer_variation_id\" IS NULL")
            .HasDatabaseName("ux_menu_definitions_parent_offer_product_id");

        builder.HasIndex(m => new { m.ParentOfferProductId, m.ParentOfferVariationId })
            .IsUnique()
            .HasFilter("\"parent_offer_product_id\" IS NOT NULL AND \"parent_offer_variation_id\" IS NOT NULL")
            .HasDatabaseName("ux_menu_definitions_parent_offer_variation");

        builder.Property(m => m.StartTime)
            .HasColumnType("time");

        builder.Property(m => m.EndTime)
            .HasColumnType("time");
    }
}
