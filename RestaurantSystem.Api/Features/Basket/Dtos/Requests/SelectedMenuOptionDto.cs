namespace RestaurantSystem.Api.Features.Basket.Dtos.Requests;

public record SelectedMenuOptionDto
{
    public Guid SectionId { get; set; }
    public Guid ItemId { get; set; }
    public int Quantity { get; set; } = 1;

    // Nested customization for this item
    public string? SpecialInstructions { get; set; }
    public List<Guid>? SelectedIngredients { get; set; }
    public List<Guid>? ExcludedIngredients { get; set; }
    public Dictionary<Guid, int>? IngredientQuantities { get; set; }
    public List<SelectedSideItemDto>? SelectedSideItems { get; set; }
}
