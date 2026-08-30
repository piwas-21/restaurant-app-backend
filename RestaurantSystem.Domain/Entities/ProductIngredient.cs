using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Represents a detailed ingredient for a product with optional/pricing information
/// </summary>
public class ProductIngredient : Entity
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = null!; // Default name (fallback)
    public bool IsOptional { get; set; } // Can customer add/remove this ingredient?
    public int MaxQuantity { get; set; } = 1; // Maximum quantity allowed for this ingredient
    public decimal Price { get; set; } // Additional price if customer adds this optional ingredient
    public bool IsIncludedInBasePrice { get; set; } // If true, price is included in base and deducted when deselected
    public bool IsActive { get; set; } = true; // Is this ingredient currently available?
    public int DisplayOrder { get; set; } // Order in which to display ingredients

    public Guid? GlobalIngredientId { get; set; } // Optional link to global ingredient definition

    /// <summary>
    /// Ingredient or sauce (S5, plan D8). Defaults to <see cref="IngredientKind.Ingredient"/>, which
    /// is also the migration's column default, so every pre-S5 row keeps its meaning with no backfill.
    /// It groups the row for the admin editor and (S6) the guest sheet; it changes NOTHING about the
    /// row's identity, so <c>IngredientQuantitiesJson</c> keys are unaffected by design.
    /// </summary>
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;

    /// <summary>
    /// Optional mutual-exclusion key (plan §9, D13). Rows of ONE product sharing the same non-null
    /// value are mutually exclusive: choosing one deselects the others, so at most one of them is
    /// ever on a line. <c>null</c> — every row before this field existed, and every row an admin
    /// leaves alone — means "belongs to no group" and behaves exactly as it did before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A KEY ON THE ROW, deliberately not a group entity: the ingredient id is what
    /// <c>OrderItem.IngredientQuantitiesJson</c> and <c>BasketItem.IngredientQuantitiesJson</c> hold,
    /// and a second table would make those bare Guids ambiguous across two tables (the same argument
    /// that chose <see cref="Kind"/> over a sauce entity, plan D8). Nothing about a row's identity,
    /// price or quantity changes here, so the JSON columns are untouched by construction.
    /// </para>
    /// <para>
    /// The rule is AT MOST ONE, never "exactly one": there is no per-group minimum and no server
    /// refusal, because a payload that selects two members is CHARGED for both and therefore
    /// overpays — the same direction the sauce cap already accepts (plan D9's stated residual). What
    /// IS enforced at write time lives in <c>Common/Validation/IngredientExclusionGroupRule.cs</c>:
    /// one kind per group, every member removable, and at most one member included in the base
    /// price — the three shapes a client could not render honestly.
    /// </para>
    /// </remarks>
    public string? ExclusionGroup { get; set; }

    /// <summary>
    /// The stored width of <see cref="ExclusionGroup"/>. It lives on the entity because both the EF
    /// configuration (Infrastructure) and the write validation (Api) must agree on it, and
    /// Infrastructure may not reference Api — a duplicated literal is how the two come to disagree.
    /// </summary>
    public const int ExclusionGroupMaxLength = 40;

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
    public virtual GlobalIngredient? GlobalIngredient { get; set; }
    public virtual ICollection<ProductIngredientDescription> Descriptions { get; set; } = [];
}
