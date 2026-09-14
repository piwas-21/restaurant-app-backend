using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IStaffCounterOrderBuilder
{
    Task<StaffCounterOrderBuild> BuildAsync(
        StaffCounterOrderRequest request, bool releaseToKitchen, CancellationToken cancellationToken);
}

public sealed record StaffCounterOrderBuild(
    Order Order, CreateOrderCommand LegacyCommand, Guid? CustomerUserId);
