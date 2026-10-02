using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Projects order rows for queue responses and optionally includes caller actions.</summary>
public interface IOrderQueueProjection
{
    OrderDto Project(Order order, bool includePermittedActions);

    Task<OrderDto> ProjectAsync(
        Order order, bool includePermittedActions, CancellationToken cancellationToken);
}
