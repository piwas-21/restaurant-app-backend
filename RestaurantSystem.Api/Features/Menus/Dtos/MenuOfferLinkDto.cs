namespace RestaurantSystem.Api.Features.Menus.Dtos;

/// <summary>
/// The narrow relationship write/read contract. It intentionally contains no bundle sections, so
/// linking or unlinking cannot trigger the historical full-replacement section-id churn.
/// </summary>
public record MenuOfferLinkDto(
    Guid MenuProductId,
    Guid? ParentOfferProductId,
    Guid? ParentOfferVariationId);
