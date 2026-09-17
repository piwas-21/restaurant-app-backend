using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Features.Catalog.Dtos;

/// <summary>
/// One commercial catalogue card and the underlying purchase targets it offers.
/// </summary>
public sealed record CatalogOfferFamilyDto
{
    /// <summary>Stable anchor product ID. Clients must not derive this from a localized name.</summary>
    public Guid Id { get; init; }

    /// <summary>The card summary shown before the guest selects a purchase mode.</summary>
    public ProductSummaryDto Anchor { get; init; } = new();

    /// <summary>Menu products linked to this anchor, one per supported source variation.</summary>
    public List<CatalogMenuOfferDto> MenuOffers { get; init; } = [];

    /// <summary>Live category assignments of the anchor; menu categories never add placements.</summary>
    public List<Guid> CategoryIds { get; init; } = [];

    /// <summary>Lowest currently orderable target price, with an active-price fallback.</summary>
    public decimal StartingPrice { get; init; }
}

/// <summary>
/// The compact menu target used by an offer-family card. The full bundle is fetched only after the
/// guest chooses it, so this endpoint never pages or serializes a bundle section graph.
/// </summary>
public sealed record CatalogMenuOfferDto
{
    public Guid ProductId { get; init; }
    public Guid? ParentVariationId { get; init; }
    public decimal Price { get; init; }
    public ItemAvailabilityDto Availability { get; init; } = new();
    public bool ScheduleAvailable { get; init; }
    public List<string>? Allergens { get; init; }
}
