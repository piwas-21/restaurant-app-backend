namespace RestaurantSystem.Api.Features.Products.Dtos;

public record MenuSectionItemDto
{
    public Guid? Id { get; init; }
    public Guid ProductId { get; init; }
    public string? ProductName { get; init; } // For display
    public decimal AdditionalPrice { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsDefault { get; init; }
    public List<string>? Ingredients { get; init; }
    public List<string>? Allergens { get; init; }
}
