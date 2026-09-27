using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class OptionSetEntry : Entity
{
    public Guid OptionSetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public Guid? GlobalIngredientId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariationId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsOptional { get; set; } = true;
    public int MaxQuantity { get; set; } = 1;
    public decimal Price { get; set; }
    public bool IsIncludedInBasePrice { get; set; }
    public bool IsRequired { get; set; }
    public decimal AdditionalPrice { get; set; }
    public bool IsDefault { get; set; }
    public string? SourceEntryId { get; set; }
    public virtual OptionSet OptionSet { get; set; } = null!;
}
