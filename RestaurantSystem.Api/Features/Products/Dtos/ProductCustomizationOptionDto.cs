namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductCustomizationIngredientOptionDto
{
    public Guid? Id { get; init; }
    public Guid ProductIngredientId { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsDefault { get; init; }
}

public record ProductCustomizationProductOptionDto
{
    public Guid? Id { get; init; }
    public Guid OptionProductId { get; init; }
    public string OptionProductName { get; init; } = string.Empty;
    public decimal AdditionalPrice { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsDefault { get; init; }
}
