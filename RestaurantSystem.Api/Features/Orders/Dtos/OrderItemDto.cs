namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>
/// Distinguishes the two kinds of child <see cref="OrderItemDto"/> that share the
/// <see cref="OrderItemDto.SideItems"/> collection: a component of a bundle/combo
/// (ProductType.Menu parent) versus a true add-on side item. Derived from the parent's
/// product type — DTO-only, no schema change (menu-bundles redesign #158). Null on the
/// top-level line item; set only on children.
/// </summary>
public enum ItemKind
{
    BundleChild,
    SideItem
}

public record OrderItemIngredientDto
{
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = null!;
    public int Quantity { get; set; }
    public bool IsRemoved { get; set; } // true if customer deselected/removed this ingredient
}

public record OrderItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuID { get; set; }
    public string ProductName { get; set; } = null!;
    public string? VariationName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ItemTotal { get; set; }
    public string? SpecialInstructions { get; set; }
    public string? KitchenType { get; set; } // FrontKitchen, BackKitchen, or None
    public List<OrderItemIngredientDto>? IngredientCustomizations { get; set; }
    public List<OrderItemDto>? SideItems { get; set; } // Child order items (additionals)
    public ItemKind? Kind { get; set; } // Child items only: bundle component vs true side item
}
