using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

/// <summary>
/// The library row had NO configuration at all before S5 — it was mapped entirely by convention.
/// This class adds the one column S5 needs and deliberately configures NOTHING else: tightening
/// <c>DefaultName</c> or <c>ImageUrl</c> here would rewrite live columns in the same migration and
/// hide a schema change inside a feature slice.
/// </summary>
public class GlobalIngredientConfiguration : IEntityTypeConfiguration<GlobalIngredient>
{
    public void Configure(EntityTypeBuilder<GlobalIngredient> builder)
    {
        // Default 0 = IngredientKind.Ingredient, which is what all 654 seeded rows are.
        builder.Property(g => g.Kind)
            .HasConversion<int>()
            .HasDefaultValue(IngredientKind.Ingredient)
            .IsRequired();

        // Default 0 = LibraryOrigin.System, which is what all 654 seeded rows are — so the column
        // lands with every existing row already correct and needs no backfill.
        builder.Property(g => g.Origin)
            .HasConversion<int>()
            .HasDefaultValue(LibraryOrigin.System)
            .IsRequired();
    }
}
