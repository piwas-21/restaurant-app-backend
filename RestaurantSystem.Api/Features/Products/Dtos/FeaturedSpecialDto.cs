using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Dtos;

/// <summary>
/// DTO for the currently featured special product
/// </summary>
public record FeaturedSpecialDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal BasePrice { get; init; }
    public string? ImageUrl { get; init; }

    /// <summary>
    /// The product's kind, mirroring <see cref="ProductSummaryDto.Type"/>. Additive — clients that
    /// ignore it are unaffected.
    /// <para>
    /// Load-bearing for admin surfaces, not decorative. A combo is not its own entity: it is a
    /// <see cref="ProductType.Menu"/> product owning a <c>MenuDefinition</c>, and nothing in
    /// <c>SetFeaturedSpecialCommand</c> stops one being featured — it checks
    /// <c>IsSpecial</c> and <c>IsActive</c> only. Without this field the banner carries no way to
    /// tell a combo from a plain product, so a client offering an inline base-price edit there
    /// would dispatch a combo to the product price endpoint.
    /// </para>
    /// <para>
    /// The hazard is the VALIDATOR, not the column. A combo does have a <c>BasePrice</c> — and it
    /// is load-bearing, since <c>BasketItemFactory</c> prices every combo line starting from it and
    /// adds the selected options on top. Both write paths reach that same column, but they do not
    /// share a rule: the bundle path requires <c>BasePrice &gt; 0</c>
    /// (<c>MenuBundleCommandValidatorBase</c>) while <c>UpdateProductPriceCommandValidator</c>
    /// allows <c>&gt;= 0</c>. Routing a combo the wrong way therefore lets an admin set a price the
    /// combo's own editor would refuse. The number also means something different on a combo — it
    /// is a starting-from price, not the price the guest pays.
    /// </para>
    /// </summary>
    public ProductType Type { get; init; }

    public DateTime FeaturedDate { get; init; }
    public int PreparationTimeMinutes { get; init; }
    public List<string>? Ingredients { get; init; }
    public List<string>? Allergens { get; init; }
    public List<ProductImageDto>? Images { get; init; }
    public List<ProductVariationDto> Variations { get; init; } = [];
    public List<SideItemDto> SuggestedSideItems { get; init; } = [];
    public List<ProductIngredientDto> DetailedIngredients { get; init; } = [];

    /// <summary>
    /// Server-resolved per-order-type availability, exactly as the catalog cards carry it. Required
    /// rather than optional: the banner is an ENTRY POINT — a guest can order straight from it — so
    /// an absent verdict here is an unguarded add, not a cosmetic gap.
    /// </summary>
    public required ItemAvailabilityDto Availability { get; init; }
}
