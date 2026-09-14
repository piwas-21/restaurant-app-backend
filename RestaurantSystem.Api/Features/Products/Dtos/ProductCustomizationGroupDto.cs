namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductCustomizationGroupDto
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsRequired { get; init; }
    public int MinSelection { get; init; }
    public int MaxSelection { get; init; }
    public int IncludedFreeUnits { get; init; }
    public bool IsActive { get; init; }
    public Dictionary<string, ProductCustomizationGroupContentDto> Content { get; init; } = [];
    public List<ProductCustomizationIngredientOptionDto> IngredientOptions { get; init; } = [];
    public List<ProductCustomizationProductOptionDto> ProductOptions { get; init; } = [];
}

public record ProductCustomizationGroupContentDto
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
