using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Represents a reusable global ingredient definition
/// </summary>
public class GlobalIngredient : SoftDeleteEntity
{
    public string DefaultName { get; set; } = null!; // Fallback name (usually English)
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Ingredient or sauce (S5, plan D8). The catalog carries the same discriminator as the
    /// per-product row so the library picker can offer sauces to the Sauces group and ingredients to
    /// the Ingredients group. Defaults to <see cref="IngredientKind.Ingredient"/>: the 654 seeded
    /// rows keep the meaning they were seeded with, and nothing is re-classified by this slice.
    /// </summary>
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;

    // Navigation properties
    public virtual ICollection<GlobalIngredientTranslation> Translations { get; set; } = [];
}
