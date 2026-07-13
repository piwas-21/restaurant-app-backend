namespace RestaurantSystem.Api.Features.Menus.Dtos;

// The menu-bundle READ/response contract (menu-bundles redesign #156, slice 4c). These types are
// deliberately distinct from the same-shaped Products.Dtos family, which is the WRITE/input contract
// for the bundle Create/Update commands (and the product-read contract). The two carry different wire
// shapes — this response family formats times as "hh:mm:ss" strings, uses non-null Guid Ids, and
// projects DetailedIngredients/Content the input family doesn't — so they can't be merged. Naming the
// response family MenuBundle* (rather than reusing MenuDefinitionDto/MenuSectionDto/… from
// Products.Dtos) removes the CS0104 collision that previously forced callers to fully-qualify.

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
    public MenuBundleDefinitionDto? MenuDefinition { get; set; }
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
/// DTO for a bundle's menu definition (response shape; times pre-formatted as strings)
/// </summary>
public class MenuBundleDefinitionDto
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
    public List<MenuBundleSectionDto> Sections { get; set; } = new();
}

/// <summary>
/// DTO for a bundle menu section
/// </summary>
public class MenuBundleSectionDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }
    public int MinSelection { get; set; }
    public int MaxSelection { get; set; }
    public List<MenuBundleSectionItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO for a bundle menu section item
/// </summary>
public class MenuBundleSectionItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductName { get; set; }
    public decimal AdditionalPrice { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public List<string>? Ingredients { get; set; }
    public List<string>? Allergens { get; set; }
    public List<MenuBundleIngredientDto>? DetailedIngredients { get; set; }
    public List<MenuBundleSuggestedSideItemDto>? SuggestedSideItems { get; set; }
}

public class MenuBundleIngredientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public decimal Price { get; set; }
    public bool IsIncludedInBasePrice { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int MaxQuantity { get; set; }
    public Dictionary<string, MenuBundleIngredientContentDto>? Content { get; set; }
}

public class MenuBundleIngredientContentDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MenuBundleSuggestedSideItemDto
{
    public Guid Id { get; set; }
    public Guid SideItemProductId { get; set; }
    public string? SideItemProductName { get; set; }
    public decimal SideItemBasePrice { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
}
