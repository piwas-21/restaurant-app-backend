namespace RestaurantSystem.Api.Features.Basket.Dtos;

public record BasketItemDto
{
    // Basket item identifier (required for updates/deletes)
    public Guid? Id { get; set; }

    // Product details
    public Guid? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductDescription { get; set; }
    public string? ProductImageUrl { get; set; }
    public Guid? ProductVariationId { get; set; }
    public string? VariationName { get; set; }

    // Menu details
    public Guid? MenuId { get; set; }
    public string? MenuName { get; set; }
    public DateOnly? MenuDate { get; set; }
    public List<MenuItemSummaryDto>? MenuItems { get; set; }

    // Common properties
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ItemTotal { get; set; }
    public string? SpecialInstructions { get; set; }

    // Customization fields for optional ingredients
    public List<Guid>? SelectedIngredients { get; set; }
    public List<Guid>? ExcludedIngredients { get; set; }
    public List<Guid>? AddedIngredients { get; set; }
    public Dictionary<Guid, int>? IngredientQuantities { get; set; } // { ingredientId: quantity }
    public decimal CustomizationPrice { get; set; }

    // Ingredient names for display purposes
    public List<string>? SelectedIngredientNames { get; set; }
    public List<string>? ExcludedIngredientNames { get; set; }
    public List<string>? AddedIngredientNames { get; set; }

    // Selected side items (with quantities)
    public List<BasketSideItemDto>? SelectedSideItems { get; set; }

    // For Menu Bundles
    public List<BasketItemDto>? ChildItems { get; set; }

    // Multilingual support
    public Dictionary<string, BasketItemVariationContentDto>? VariationContent { get; set; }
}

public record BasketItemVariationContentDto(string Name, string? Description);
