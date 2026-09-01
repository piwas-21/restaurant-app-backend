using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class Product : SoftDeleteEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public string? ImageUrl { get; init; } // Primary image URL for backward compatibility
    public bool IsActive { get; set; } = true;
    public bool IsAvailable { get; set; } = true;
    public bool IsSpecial { get; set; } // Is this a special menu (e.g., holiday menu)

    /// <summary>
    /// When <c>true</c>, the synthetic "base product" option (ordering with no variation) is not
    /// offered: the guest must pick one of the variations. Stored as-is; the EFFECTIVE rule adds
    /// "…and at least one variation is active", so a product whose variations are all deactivated
    /// stays orderable instead of going silently dead. Read it through
    /// <c>Api.Features.Catalog.BaseProductVisibility</c>, never bare.
    /// </summary>
    public bool HideBaseProduct { get; set; }

    /// <summary>
    /// A bundle COMPONENT, not a catalogue item: referenced by a <see cref="MenuSection"/>, never
    /// listed, never orderable alone. A DIFFERENT AXIS from <see cref="HideBaseProduct"/>, which
    /// degrades to <c>false</c> with no active variation. Enforced by the catalogue queries and by
    /// <c>BasketComponentGuard</c>, on TOP-LEVEL lines only — bundle children stay choosable.
    /// </summary>
    public bool IsComponent { get; set; }

    public bool IsFeaturedSpecial { get; set; } // Is this the featured/highlighted special of the day
    public DateTime? FeaturedDate { get; set; } // Date when this was set as featured
    public int PreparationTimeMinutes { get; set; }
    public ProductType Type { get; set; } = ProductType.MainItem;
    public KitchenType KitchenType { get; set; } = KitchenType.None; // Front or Back kitchen designation
    public List<string>? Ingredients { get; set; } // JSON array of ingredients
    public List<string>? Allergens { get; set; } // JSON array of allergens
    public int DisplayOrder { get; set; }

    /// <summary>
    /// The <see cref="Common.Enums.OrderChannels"/> bitmask this product may be ordered through.
    /// <c>null</c> = INHERIT from the primary category (which may itself be null = every channel).
    /// Inheritance is all-or-nothing: a product either inherits fully or overrides fully.
    /// Always read via <see cref="Common.OrderChannelMap"/> — never cast.
    /// </summary>
    public int? AvailableOrderTypes { get; set; }

    /// <summary>
    /// How many <see cref="Common.Enums.IngredientKind.Sauce"/> rows a guest MUST choose. 0 = none
    /// required, which is what every product gets and what every product had before S5.
    /// </summary>
    /// <remarks>
    /// <b>These three are admin-editable per product, and carry NO tenant default (owner ruling,
    /// plan §7 Q3, 2026-08-27).</b> A restaurant that gives one sauce away sets it in the item
    /// editor; there is deliberately no "1 free sauce" rule in code or configuration, because that
    /// is a RUMI fact and this is a multi-tenant product. The seeded values are the neutral ones an
    /// ingredient already has today: nothing required, no group cap, nothing free.
    /// <para>
    /// They are also the WHOLE of the group rule. There is no general min/max-select engine here and
    /// this is not a step towards one being smuggled in (plan §7 Q2).
    /// </para>
    /// </remarks>
    public int SauceMin { get; set; }

    /// <summary>
    /// The most distinct active sauce rows a guest may choose, or <c>null</c> for NO group cap.
    /// <see cref="Api.Common.Validation.SauceSelectionRule"/> enforces it at basket and direct-order
    /// selection writers; a row's own <see cref="ProductIngredient.MaxQuantity"/> remains separate.
    /// </summary>
    /// <remarks>
    /// Nullable rather than "0 means unlimited": 0 is a perfectly meaningful cap (a product that
    /// takes no sauces at all), so overloading it would make the two states indistinguishable and
    /// would be exactly the magic number the workspace conventions forbid.
    /// </remarks>
    public int? SauceMax { get; set; }

    /// <summary>
    /// How many chosen sauces are free before the per-row price starts applying. 0 = none, so no
    /// product changes price because of S5.
    /// </summary>
    /// <remarks>
    /// <b>S5 stores this and prices nothing with it.</b> Ingredient money has exactly one writer —
    /// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c> — and the rule that reads
    /// this value lands there in S6 (plan D10). Do not add a second place that computes money from
    /// it.
    /// </remarks>
    public int SauceIncludedFree { get; set; }

    // Navigation properties
    public virtual ICollection<ProductImage> Images { get; set; } = [];
    public virtual ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
    public virtual ICollection<ProductVariation> Variations { get; set; } = [];
    public virtual ICollection<ProductSideItem> SuggestedSideItems { get; set; } = [];
    public virtual ICollection<ProductIngredient> DetailedIngredients { get; set; } = [];
    public virtual ICollection<MenuItem> MenuProducts { get; set; } = [];
    public virtual ICollection<ProductDescription> Descriptions { get; set; } = [];
    public virtual MenuDefinition? MenuDefinition { get; set; }
}
