using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal BasePrice { get; init; }
    public string? ImageUrl { get; init; } // Primary image URL for backward compatibility
    public bool IsActive { get; init; }
    public bool IsAvailable { get; init; }

    public bool IsSpecial { get; init; } // Is this a special menu (e.g., holiday menu)

    /// <summary>
    /// When <c>true</c> the base row ("order it with no variation") is not offered — the guest must
    /// choose a variation. This is the STORED flag, so an admin editor round-trips it unharmed; the
    /// effective rule is "…and at least one variation is active", which readers apply themselves
    /// (server-side: <c>BasketBaseProductGuard</c>). Additive — clients that ignore it are unaffected.
    /// </summary>
    public bool HideBaseProduct { get; init; }

    /// <summary>
    /// Bundle-only component: excluded from listings and refused as a top-level basket line.
    /// Semantics on <see cref="Domain.Entities.Product.IsComponent"/>. Additive.
    /// </summary>
    public bool IsComponent { get; init; }

    public int PreparationTimeMinutes { get; init; }
    public ProductType Type { get; init; }
    public KitchenType KitchenType { get; init; } // Front or Back kitchen designation
    public List<string>? Ingredients { get; init; } = [];
    public List<ProductIngredientDto>? DetailedIngredients { get; init; } = [];
    public List<string>? Allergens { get; init; } = [];
    public int DisplayOrder { get; init; }
    public ProductDescriptionsDto Content { get; set; } = new();

    public List<ProductImageDto> Images { get; init; } = [];
    public List<ProductCategoryDto> Categories { get; init; } = [];
    public CategoryDto? PrimaryCategory { get; init; }
    public List<ProductVariationDto> Variations { get; init; } = [];
    public List<SideItemDto> SuggestedSideItems { get; init; } = [];
    /// <summary>
    /// A bundle's schedule and sections, in the same shape <c>GET /api/Menus/{id}</c> serves
    /// (#468). It used to be the WRITE contract's <c>MenuDefinitionDto</c>, whose section items
    /// carry an option's identity and price and nothing else — so a reader that opened a bundle by
    /// PRODUCT id got a combo with no recipe, no sauce rule and no allergens, and had nothing to
    /// customize. The two families are documented in <c>MenuBundleDto.cs</c>: <c>MenuBundle*</c> is
    /// the READ contract, and this is a read.
    /// </summary>
    /// <remarks>
    /// Additive on the wire: every key the old shape carried is still here, with the same name and
    /// the same JSON (<c>TimeSpan</c> and the mapper's <c>hh:mm:ss</c> string serialize
    /// identically), plus the seven the option rows were missing. The REQUEST direction is
    /// unchanged — the product and bundle commands still take <c>MenuDefinitionDto</c>.
    /// </remarks>
    public MenuBundleDefinitionDto? MenuDefinition { get; init; }

    /// <summary>Resolved per-order-type availability. Additive.</summary>
    public ItemAvailabilityDto Availability { get; init; } = new();

    /// <summary>
    /// The raw <c>OrderChannels</c> mask stored on the item (<c>null</c> = inherit from the primary
    /// category). Admin editors need the stored value; customers read <see cref="Availability"/>.
    /// </summary>
    public int? AvailableOrderTypes { get; init; }

    /// <summary>Sauce group rule (S5). Semantics, and why max is nullable, on <see cref="Domain.Entities.Product"/>.</summary>
    public int SauceMin { get; init; }

    /// <inheritdoc cref="SauceMin"/>
    public int? SauceMax { get; init; }

    /// <inheritdoc cref="SauceMin"/>
    public int SauceIncludedFree { get; init; }
}
