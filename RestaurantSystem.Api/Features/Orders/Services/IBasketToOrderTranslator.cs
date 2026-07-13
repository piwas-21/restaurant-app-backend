using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Translates a persisted basket (as its mapped <see cref="BasketItemDto"/> tree) into the
/// <see cref="CreateOrderItemDto"/> payload the order pipeline consumes. This is the single,
/// server-side source of truth for the basket→order money-path transform, replacing the former
/// client-owned <c>utils/orderItemsPayload.ts</c> (menu-bundles redesign #157, slice 5).
/// </summary>
public interface IBasketToOrderTranslator
{
    List<CreateOrderItemDto> Translate(IEnumerable<BasketItemDto> basketItems);
}
