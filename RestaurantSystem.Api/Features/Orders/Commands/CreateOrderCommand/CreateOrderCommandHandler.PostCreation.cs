using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public partial class CreateOrderCommandHandler
{
    private async Task<ApiResponse<OrderDto>> CompletePostCreationAsync(
        Order order, bool paysOnline, CancellationToken cancellationToken)
    {
        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);
        orderDto.PermittedActions = _permittedActionsService.GetPermittedActions(order);
        await _notifications.NotifyOrderCreatedAsync(orderDto);
        await _notifications.NotifyFocusOrderUpdateAsync(orderDto);
        await _tableReservation.ReserveForDineInAsync(order, cancellationToken);

        if (!paysOnline)
            await _notifications.SendNewOrderMailAsync(order, orderDto, cancellationToken);

        _logger.LogInformation("Order {OrderNumber} created successfully by user {UserId}",
            order.OrderNumber, _currentUserService.UserId);
        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order created successfully");
    }
}
