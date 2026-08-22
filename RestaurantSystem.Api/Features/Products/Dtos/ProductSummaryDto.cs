using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal BasePrice { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsSpecial { get; init; }

    /// <summary>
    /// When <c>true</c> the base row ("order it with no variation") is not offered — the guest must
    /// choose a variation. This is the STORED flag, so an admin editor round-trips it unharmed; the
    /// effective rule is "…and at least one variation is active", which readers apply themselves
    /// (server-side: <c>BasketBaseProductGuard</c>). Additive — clients that ignore it are unaffected.
    /// </summary>
    public bool HideBaseProduct { get; init; }

    public ProductType Type { get; init; }
    public List<string>? Ingredients { get; init; } = [];
    public List<ProductIngredientDto>? DetailedIngredients { get; init; } = [];
    public List<string>? Allergens { get; init; } = [];
    public List<string> CategoryNames { get; init; } = new();
    public List<ProductImageDto> Images { get; init; } = [];
    public ProductDescriptionsDto Content { get; set; } = new();
    public string? PrimaryCategoryName { get; init; }
    public int VariationCount { get; init; }
    public List<ProductVariationDto>? Variations { get; init; } = [];
    public List<SideItemDto> SuggestedSideItems { get; init; } = [];

    /// <summary>
    /// Resolved per-order-type availability. Additive — clients that ignore it are unaffected.
    /// </summary>
    public ItemAvailabilityDto Availability { get; init; } = new();

    /// <summary>
    /// The raw <c>OrderChannels</c> mask stored on the item (<c>null</c> = inherit). Admin editors
    /// need the stored value; customers should read <see cref="Availability"/> instead.
    /// </summary>
    public int? AvailableOrderTypes { get; init; }
}
