namespace RestaurantSystem.Api.Features.Products.Dtos;

public record UpdateProductVariationDto
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal PriceModifier { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }
}
