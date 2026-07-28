using RestaurantSystem.Api.Features.Catalog.Dtos;

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

    /// <summary>
    /// Server-resolved per-order-type availability, exactly as the catalog cards carry it. Required
    /// rather than optional: the banner is an ENTRY POINT — a guest can order straight from it — so
    /// an absent verdict here is an unguarded add, not a cosmetic gap.
    /// </summary>
    public required ItemAvailabilityDto Availability { get; init; }
}
