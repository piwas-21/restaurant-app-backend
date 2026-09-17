namespace RestaurantSystem.Api.Features.Menus.Dtos;

/// <summary>Payload for changing only a menu's offer-family parent relationship.</summary>
public sealed record MenuOfferParentRequestDto(
    Guid? ParentOfferProductId,
    Guid? ParentOfferVariationId = null);
