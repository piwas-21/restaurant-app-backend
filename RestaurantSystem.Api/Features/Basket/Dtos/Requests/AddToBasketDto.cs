using RestaurantSystem.Domain.Common.Enums;

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
    public List<Guid>? AddedIngredients { get; set; }
    public Dictionary<Guid, int>? IngredientQuantities { get; set; } // { ingredientId: quantity }
    public List<CustomizationGroupSelectionDto>? CustomizationSelections { get; set; }

    // Selected side items with quantities
    public List<SelectedSideItemDto>? SelectedSideItems { get; set; }

    // Selected menu options (for Menu type products)
    public List<SelectedMenuOptionDto>? SelectedMenuOptions { get; set; }
}

public record SelectedSideItemDto
{
    /// <summary>The selected product identity.</summary>
    public Guid Id { get; set; }
    /// <summary>Stable identity of the ProductSideItem association row that offered this product.</summary>
    public Guid? SuggestedSideItemId { get; set; }
    public int Quantity { get; set; }
    public Guid? ProductVariationId { get; set; }
    public int? PresentationOrder { get; set; }
    public CompositionRole? CompositionRole { get; set; }
}
