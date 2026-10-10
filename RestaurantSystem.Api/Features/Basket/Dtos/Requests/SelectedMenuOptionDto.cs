namespace RestaurantSystem.Api.Features.Basket.Dtos.Requests;

public record SelectedMenuOptionDto
{
    public Guid SectionId { get; set; }
    /// <summary>Stable identity of the selected row within the named menu section.</summary>
    public Guid? MenuSectionItemId { get; set; }
    public Guid ItemId { get; set; }
    /// <summary>Fixed variation attached to this menu row, when present.</summary>
    public Guid? ProductVariationId { get; set; }
    /// <summary>Optional variation selected from the component product's own variation list.</summary>
    public Guid? ComponentProductVariationId { get; set; }
    public int Quantity { get; set; } = 1;

    // Nested customization for this item
    public string? SpecialInstructions { get; set; }
    public List<Guid>? SelectedIngredients { get; set; }
    public Dictionary<Guid, int>? IngredientQuantities { get; set; }
    public List<CustomizationGroupSelectionDto>? CustomizationSelections { get; set; }
    /// <summary>Suggested side selections for this exact bundle component.</summary>
    public List<SelectedSideItemDto>? SelectedSideItems { get; set; }
}
