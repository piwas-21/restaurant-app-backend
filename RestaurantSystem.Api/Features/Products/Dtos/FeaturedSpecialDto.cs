namespace RestaurantSystem.Api.Features.Products.Dtos;

/// <summary>
/// DTO for the currently featured special product
/// </summary>
public record FeaturedSpecialDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal BasePrice { get; init; }
    public string? ImageUrl { get; init; }
    public DateTime FeaturedDate { get; init; }
    public int PreparationTimeMinutes { get; init; }
    public List<string>? Ingredients { get; init; }
    public List<string>? Allergens { get; init; }
    public List<ProductImageDto>? Images { get; init; }
    public List<ProductVariationDto> Variations { get; init; } = [];
    public List<SideItemDto> SuggestedSideItems { get; init; } = [];
    public List<ProductIngredientDto> DetailedIngredients { get; init; } = [];
}
