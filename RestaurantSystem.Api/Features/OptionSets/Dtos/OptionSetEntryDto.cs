namespace RestaurantSystem.Api.Features.OptionSets.Dtos;

public sealed class OptionSetEntryDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public Guid? GlobalIngredientId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public bool IsOptional { get; set; } = true;
    public int MaxQuantity { get; set; } = 1;
    public decimal Price { get; set; }
    public bool IsIncludedInBasePrice { get; set; }
    public bool IsRequired { get; set; }
    public decimal AdditionalPrice { get; set; }
    public bool IsDefault { get; set; }
}
