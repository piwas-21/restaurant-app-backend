namespace RestaurantSystem.Api.Features.Basket.Dtos.Requests;

public record AddToBasketDto
{
    public Guid ProductId { get; set; } = Guid.Empty;
    public Guid? ProductVariationId { get; set; }
    public Guid? MenuId { get; set; }
    public int Quantity { get; set; } = 1;
    public string? SpecialInstructions { get; set; }

    // Customization fields for optional ingredients
    public List<Guid>? SelectedIngredients { get; set; }
    public List<Guid>? ExcludedIngredients { get; set; }
    public List<Guid>? AddedIngredients { get; set; }
    public Dictionary<Guid, int>? IngredientQuantities { get; set; } // { ingredientId: quantity }

    // Selected side items with quantities
    public List<SelectedSideItemDto>? SelectedSideItems { get; set; }

    // Selected menu options (for Menu type products)
    public List<SelectedMenuOptionDto>? SelectedMenuOptions { get; set; }
}

public record SelectedSideItemDto
{
    public Guid Id { get; set; }
    public int Quantity { get; set; }
}
