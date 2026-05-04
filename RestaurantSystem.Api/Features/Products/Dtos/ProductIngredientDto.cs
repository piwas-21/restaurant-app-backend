namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductIngredientDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsOptional { get; set; }
    public decimal Price { get; set; }
    public bool IsIncludedInBasePrice { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int MaxQuantity { get; set; }
    public Dictionary<string, ProductIngredientContentDto>? Content { get; set; }
}

public record ProductIngredientContentDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}
