using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Categories.Dtos;
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
    public MenuDefinitionDto? MenuDefinition { get; init; }

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
