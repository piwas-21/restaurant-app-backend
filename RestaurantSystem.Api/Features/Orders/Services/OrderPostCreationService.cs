using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed class OrderPostCreationService(IOrderMappingService mapping, IOrderPermittedActionsService actions,
    IOrderNotificationService notifications, IOrderTableReservationService reservations,
    ICurrentUserService currentUser, ILogger<OrderPostCreationService> logger) : IOrderPostCreationService
{
    public async Task<ApiResponse<OrderDto>> CompleteAsync(Order order, bool paysOnline, CancellationToken cancellationToken)
    {
        var dto = await mapping.MapToOrderDtoAsync(order, cancellationToken);
        dto.PermittedActions = actions.GetPermittedActions(order);
        await notifications.NotifyOrderCreatedAsync(dto);
        await notifications.NotifyFocusOrderUpdateAsync(dto);
        await reservations.ReserveForDineInAsync(order, cancellationToken);
        if (!paysOnline) await notifications.SendNewOrderMailAsync(order, dto, cancellationToken);
        logger.LogInformation("Order {OrderNumber} created successfully by user {UserId}", order.OrderNumber, currentUser.UserId);
        return ApiResponse<OrderDto>.SuccessWithData(dto, "Order created successfully");
    }
}
