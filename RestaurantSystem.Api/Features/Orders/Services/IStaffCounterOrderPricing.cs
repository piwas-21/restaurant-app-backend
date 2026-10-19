using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IStaffCounterOrderPricing
{
    Task<List<CreateOrderItemDto>> PriceBasketAsync(
        BasketDto basket, List<CreateOrderItemDto> translated, CancellationToken cancellationToken);

    Task<List<CreateOrderItemDto>> PriceAsync(
        IReadOnlyCollection<CreateOrderItemDto> items, CancellationToken cancellationToken);
}
