using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductVariation</c> had NO configuration at all (backend analysis §9 defect 3): the whole
/// entity was mapped by convention, which left <c>name</c> and <c>description</c> as unbounded
/// <c>text</c> and <c>price_modifier</c> as an unqualified <c>numeric</c> — the only money column in
/// the schema without a scale, next to <c>ProductIngredient.Price</c>'s <c>decimal(18,2)</c>.
///
/// <para>
/// The lengths here are deliberately WIDER than the validator's (50 name / 200 description). The
/// validator is what a new write must satisfy; the column is what every row already in the table
/// must satisfy, and until this slice <c>UpdateProductCommandValidator</c> applied no variation
/// rules at all — so a long name could have been saved by any PUT ever made. Bounding the column at
/// the validator's number would turn that possibility into a failed migration on a live box.
/// </para>
/// </summary>
public class ProductVariationConfiguration : IEntityTypeConfiguration<ProductVariation>
{
    public void Configure(EntityTypeBuilder<ProductVariation> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(v => v.Description)
            .HasMaxLength(500);

        builder.Property(v => v.PriceModifier)
            .HasColumnType("decimal(18,2)");

        builder.Property(v => v.IsActive)
            .IsRequired();

        builder.Property(v => v.DisplayOrder)
            .IsRequired();

        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variations)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Provenance (S4). NO ACTION, like the ingredient link: archiving or deleting a library row
        // must never reach into the products that copied it, and the write path is what refuses a
        // NEW link to a row that is off the shelf.
        builder.HasOne(v => v.GlobalVariation)
            .WithMany()
            .HasForeignKey(v => v.GlobalVariationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(v => v.ProductId);
        builder.HasIndex(v => v.GlobalVariationId);
    }
}
