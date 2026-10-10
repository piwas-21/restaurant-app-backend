using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Dtos;

public record SideItemDto
{
    public Guid Id { get; init; }
    /// <summary>Stable identity of the ProductSideItem association row; Id remains SideItemProductId.</summary>
    public Guid SuggestedSideItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public ProductType Type { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
    public List<ProductImageDto> Images { get; init; } = [];

}
