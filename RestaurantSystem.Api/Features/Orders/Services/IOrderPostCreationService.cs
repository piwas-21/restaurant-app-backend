using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderPostCreationService
{
    Task<ApiResponse<OrderDto>> CompleteAsync(Order order, bool paysOnline, CancellationToken cancellationToken);
}
