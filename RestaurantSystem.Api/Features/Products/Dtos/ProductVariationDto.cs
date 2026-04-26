namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductVariationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal PriceModifier { get; init; }
    public decimal FinalPrice { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }
    public Dictionary<string, ProductVariationContentDto>? Content { get; init; }
}

public record ProductVariationContentDto
{
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
}
