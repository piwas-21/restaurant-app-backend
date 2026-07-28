using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;

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
    public List<ProductImageDto> Images { get; set; } = new();

    /// <summary>
    /// Server-resolved per-order-type availability for the requested channel — the same field, from
    /// the same resolver, that <c>ProductSummaryDto</c> carries (ORDER-TYPE-AVAILABILITY-PLAN §9.2).
    /// </summary>
    /// <remarks>
    /// Judges the BUNDLE's own mask (its override, else its primary category's), not its options'.
    /// A bundle whose optional side is takeaway-only is still orderable on dine-in — the guest picks
    /// a different side — so intersecting the children here would block sellable combos. Making a
    /// bundle unorderable when a REQUIRED section has no option on the channel is the genuinely
    /// correct child-derived rule and is deferred (plan §8, "bundle ↔ child intersection"); until it
    /// lands, <c>BasketChannelGuard</c> refuses the blocked component at add time (§9.3).
    /// </remarks>
    public ItemAvailabilityDto Availability { get; set; } = new();

    /// <summary>
    /// The bundle's OWN stored channel mask — <c>null</c> = inherit from the primary category.
    /// Admin-facing, so the editor can echo it back on save. Read <see cref="Availability"/>, never
    /// this, for a verdict.
    /// </summary>
    /// <remarks>
    /// <b>No client reads or writes this yet.</b> `baseMenuBundleSchema` has no such key and the
    /// editor renders its order-type control only for non-bundles, so until the frontend half of
    /// §9.2 lands this field is emitted and ignored — and, because the bundle PUT assigns the column
    /// unconditionally, a bundle mask set out of band is CLEARED by any unrelated bundle save. See
    /// <c>UpdateMenuBundleCommandHandler</c>.
    /// </remarks>
    public int? AvailableOrderTypes { get; set; }
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
