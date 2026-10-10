using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class BasketItem : Entity
{
    public Guid BasketId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuId { get; set; }
    /// <summary>Frozen membership of a validated bundle choice; no catalog FK.</summary>
    public Guid? SectionId { get; set; }
    /// <summary>Stable selected menu-section row identity; no catalog FK.</summary>
    public Guid? MenuSectionItemId { get; set; }
    public Guid? ParentComponentMenuSectionItemId { get; set; }
    public QuantityBasis? QuantityBasis { get; set; }
    public ConfigurationScope? ConfigurationScope { get; set; }
    public CompositionRole? CompositionRole { get; set; }
    public string? PresentationLabel { get; set; }
    public int? PresentationOrder { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ItemTotal { get; set; }
    public string? SpecialInstructions { get; set; }

    // Customization fields for optional ingredients
    public List<Guid>? SelectedIngredients { get; set; } // IDs of selected optional ingredients
    public List<Guid>? AddedIngredients { get; set; } // IDs of optional ingredients added
    public string? IngredientQuantitiesJson { get; set; } // JSON: { ingredientId: quantity }
    /// <summary>Server-verified authored ingredient roles, frozen when the basket selection is accepted.</summary>
    public string? IngredientCompositionRolesJson { get; set; }
    public decimal CustomizationPrice { get; set; } // Additional price from customizations

    // Selected side items (stored as JSON: {id, quantity} pairs)
    public string? SelectedSideItemsJson { get; set; }

    // Navigation properties
    public virtual Basket Basket { get; set; } = null!;
    public virtual Product? Product { get; set; }
    public virtual ProductVariation? ProductVariation { get; set; }
    public virtual Menu? Menu { get; set; }

    public Guid? ParentBasketItemId { get; set; }
    /// <summary>The explicit product-option membership represented by this child row.</summary>
    public Guid? ProductCustomizationOptionId { get; set; }
    public virtual BasketItem? ParentBasketItem { get; set; }
    public virtual ICollection<BasketItem> ChildBasketItems { get; set; } = new List<BasketItem>();
}
