namespace RestaurantSystem.Api.Features.Menus.Dtos;

/// <summary>
/// DTO for menu bundle responses - excludes product-specific fields
/// </summary>
public class MenuBundleDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsSpecial { get; set; }
    public int PreparationTimeMinutes { get; set; }
    public string Type { get; set; } = "menu";
    public int DisplayOrder { get; set; }
    public MenuDefinitionDto? MenuDefinition { get; set; }
    public Dictionary<string, MenuBundleContentDto> Content { get; set; } = new();
    public List<RestaurantSystem.Api.Features.Products.Dtos.ProductImageDto> Images { get; set; } = new();
}

/// <summary>
/// DTO for menu bundle multilingual content
/// </summary>
public class MenuBundleContentDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// DTO for menu definition
/// </summary>
public class MenuDefinitionDto
{
    public Guid Id { get; set; }
    public bool IsAlwaysAvailable { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public bool AvailableMonday { get; set; }
    public bool AvailableTuesday { get; set; }
    public bool AvailableWednesday { get; set; }
    public bool AvailableThursday { get; set; }
    public bool AvailableFriday { get; set; }
    public bool AvailableSaturday { get; set; }
    public bool AvailableSunday { get; set; }
    public List<MenuSectionDto> Sections { get; set; } = new();
}

/// <summary>
/// DTO for menu section
/// </summary>
public class MenuSectionDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }
    public int MinSelection { get; set; }
    public int MaxSelection { get; set; }
    public List<MenuSectionItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO for menu section item
/// </summary>
public class MenuSectionItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductName { get; set; }
    public decimal AdditionalPrice { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public List<string>? Ingredients { get; set; }
    public List<string>? Allergens { get; set; }
    public List<ProductIngredientDto>? DetailedIngredients { get; set; }
    public List<SuggestedSideItemDto>? SuggestedSideItems { get; set; }
}

public class ProductIngredientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public decimal Price { get; set; }
    public bool IsIncludedInBasePrice { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int MaxQuantity { get; set; }
    public Dictionary<string, ProductIngredientContentDto>? Content { get; set; }
}

public class ProductIngredientContentDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class SuggestedSideItemDto
{
    public Guid Id { get; set; }
    public Guid SideItemProductId { get; set; }
    public string? SideItemProductName { get; set; }
    public decimal SideItemBasePrice { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
}
