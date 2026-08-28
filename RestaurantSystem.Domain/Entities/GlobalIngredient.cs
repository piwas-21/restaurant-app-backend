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

    /// <summary>
    /// When set, the row is ARCHIVED (plan D4): off the shelf, so no picker offers it and no new
    /// product may link to it, while every product that already links to it keeps both its
    /// provenance and the translations it renders.
    ///
    /// <para>
    /// This is deliberately NOT <see cref="Common.Base.SoftDeleteEntity.IsDeleted"/>. A soft delete
    /// is hidden by the global query filter, so it is invisible to every read in the application —
    /// including the includes a product detail resolves, which is why deleting a used library row
    /// silently empties that product's ingredient translations. Archiving is a state the catalog
    /// still admits to: the row stays readable, `DELETE` on a row in use produces this instead of a
    /// delete, and <c>restore</c> reverses it. Removal survives only for a row nothing uses.
    /// </para>
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>Who archived it — <c>ICurrentUserService.GetAuditIdentifier()</c>, as every other stamp.</summary>
    public string? ArchivedBy { get; set; }

    // Navigation properties
    public virtual ICollection<GlobalIngredientTranslation> Translations { get; set; } = [];
}
