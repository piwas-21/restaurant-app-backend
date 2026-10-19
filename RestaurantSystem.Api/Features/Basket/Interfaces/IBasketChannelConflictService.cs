using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

public interface IBasketChannelConflictService
{
    Task<List<BasketChannelConflictDto>> FindAsync(Domain.Entities.Basket basket, OrderType orderType,
        CancellationToken cancellationToken);
}
