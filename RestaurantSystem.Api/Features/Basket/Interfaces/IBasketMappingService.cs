using RestaurantSystem.Api.Features.Basket.Dtos;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

/// <summary>
/// Maps a <see cref="RestaurantSystem.Domain.Entities.Basket"/> entity to its
/// <see cref="BasketDto"/> wire representation — resolving ingredient/side-item
/// names, deserialising the customisation JSON columns, nesting child items
/// under their parents, and surfacing the user's best applicable discount.
/// Extracted from <c>BasketService</c> (Sprint 3 god-class decomposition).
/// </summary>
public interface IBasketMappingService
{
    Task<BasketDto> MapAsync(RestaurantSystem.Domain.Entities.Basket basket);
}
