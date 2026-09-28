using RestaurantSystem.Api.Features.Catalog.Dtos;

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
    /// <summary>False for inactive or soft-deleted target products.</summary>
    public bool OptionProductIsActive { get; init; }
    /// <summary>False for unavailable or soft-deleted target products.</summary>
    public bool OptionProductIsAvailable { get; init; }
    /// <summary>Target product availability resolved for the requested order type.</summary>
    public ItemAvailabilityDto Availability { get; init; } = new();
    public decimal AdditionalPrice { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsDefault { get; init; }
}
