namespace RestaurantSystem.Api.Features.Products.Dtos;

public record SideItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
    public List<ProductImageDto> Images { get; init; } = [];

}
