using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IStaffCounterOrderPricing
{
    Task<List<CreateOrderItemDto>> PriceAsync(
        IReadOnlyCollection<CreateOrderItemDto> items, CancellationToken cancellationToken);
}
