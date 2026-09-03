using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// A reusable VARIATION name — "Small", "50 cl", "6 pieces" — with its translations (plan S4).
///
/// <para>
/// The catalog carries the name and the nine translations only, never a price: a variation's
/// per-product fact is <see cref="ProductVariation.PriceModifier"/>, and it is more product-specific
/// than an ingredient's price (+2.00 for a large pizza, +0.50 for a large coffee). So this is a
/// template the admin copies, exactly as the ingredient library is, and the price is always typed
/// per product. That is also where the value is: the sizes repeat across a menu and the prices do
/// not, so what a pick saves is the nine translations.
/// </para>
///
/// <para>
/// It mirrors <see cref="GlobalIngredient"/> deliberately, including the archive state S3 added,
/// because that shape has already survived two slices: pick, provenance, archive, restore.
/// </para>
/// </summary>
public class GlobalVariation : SoftDeleteEntity
{
    public string DefaultName { get; set; } = string.Empty; // Fallback name (usually English)
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Platform seed or this tenant's own (plan D14). Defaults to <see cref="LibraryOrigin.System"/>
    /// so the rows that exist when the column lands keep meaning what they meant — every one of them
    /// was seeded — and so a writer that forgets to stamp it produces an archivable-but-not-deletable
    /// row rather than a deletable built-in.
    /// </summary>
    public LibraryOrigin Origin { get; set; } = LibraryOrigin.System;

    /// <summary>
    /// Archived (plan D4): off the shelf, so no picker offers it and no new product may link to it,
    /// while every product that already links to it keeps its provenance. NOT
    /// <see cref="Common.Base.SoftDeleteEntity.IsDeleted"/>, which the global query filter hides
    /// from every read in the application — see <see cref="GlobalIngredient.ArchivedAt"/> for the
    /// full reasoning, which S3 paid for.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>Who archived it — <c>ICurrentUserService.GetAuditIdentifier()</c>, as every other stamp.</summary>
    public string? ArchivedBy { get; set; }

    // Navigation properties
    public virtual ICollection<GlobalVariationTranslation> Translations { get; set; } = [];
}
