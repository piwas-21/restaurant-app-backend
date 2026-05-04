using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal BasePrice { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsSpecial { get; init; }
    public ProductType Type { get; init; }
    public List<string>? Ingredients { get; init; } = [];
    public List<ProductIngredientDto>? DetailedIngredients { get; init; } = [];
    public List<string>? Allergens { get; init; } = [];
    public List<string> CategoryNames { get; init; } = new();
    public List<ProductImageDto> Images { get; init; } = [];
    public ProductDescriptionsDto Content { get; set; } = new();
    public string? PrimaryCategoryName { get; init; }
    public int VariationCount { get; init; }
    public List<ProductVariationDto>? Variations { get; init; } = [];
    public List<SideItemDto> SuggestedSideItems { get; init; } = [];
}
