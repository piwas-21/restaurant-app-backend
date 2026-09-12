using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderResponseProjector
{
    Task<OrderDto> ProjectAsync(Order order, CancellationToken cancellationToken);
}
