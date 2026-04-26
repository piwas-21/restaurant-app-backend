using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;
public class BasketItem : Entity
{
    public Guid BasketId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ItemTotal { get; set; }
    public string? SpecialInstructions { get; set; }

    // Customization fields for optional ingredients
    public List<Guid>? SelectedIngredients { get; set; } // IDs of selected optional ingredients
    public List<Guid>? ExcludedIngredients { get; set; } // IDs of default ingredients to exclude
    public List<Guid>? AddedIngredients { get; set; } // IDs of optional ingredients added
    public string? IngredientQuantitiesJson { get; set; } // JSON: { ingredientId: quantity }
    public decimal CustomizationPrice { get; set; } = 0; // Additional price from customizations

    // Selected side items (stored as JSON: {id, quantity} pairs)
    public string? SelectedSideItemsJson { get; set; }

    // Navigation properties
    public virtual Basket Basket { get; set; } = null!;
    public virtual Product? Product { get; set; } = null!;
    public virtual ProductVariation? ProductVariation { get; set; } = null;
    public virtual Menu? Menu { get; set; } = null!;

    public Guid? ParentBasketItemId { get; set; }
    public virtual BasketItem? ParentBasketItem { get; set; }
    public virtual ICollection<BasketItem> ChildBasketItems { get; set; } = new List<BasketItem>();
}
