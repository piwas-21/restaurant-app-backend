using RestaurantSystem.Api.Features.Basket.Dtos.Requests;

namespace RestaurantSystem.Api.Features.Products.Dtos.Requests;

/// <summary>Basket-compatible selection payload used for a side-effect-free product quote.</summary>
public sealed record ProductQuoteRequestDto
{
    public Guid? ProductVariationId { get; init; }
    public int Quantity { get; init; } = 1;
    public string? SpecialInstructions { get; init; }
    public List<Guid>? SelectedIngredients { get; init; }
    public List<Guid>? AddedIngredients { get; init; }
    public Dictionary<Guid, int>? IngredientQuantities { get; init; }
    public List<CustomizationGroupSelectionDto>? CustomizationSelections { get; init; }
    public List<SelectedSideItemDto>? SelectedSideItems { get; init; }
    public List<SelectedMenuOptionDto>? SelectedMenuOptions { get; init; }
}
