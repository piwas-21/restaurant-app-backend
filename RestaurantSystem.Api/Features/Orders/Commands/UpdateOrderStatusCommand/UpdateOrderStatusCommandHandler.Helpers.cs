using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public partial class UpdateOrderStatusCommandHandler
{
    /// <summary>
    /// The server owns the long-preparation decision. Clients always request Confirmed with their
    /// chosen minutes; this handler promotes the request to PendingApproval when customer consent
    /// is required, so web/mobile/printer clients cannot drift on the threshold.
    /// </summary>
    private void NormalizePreparationApprovalStatus(UpdateOrderStatusCommand command)
    {
        if (command.NewStatus == OrderStatus.Confirmed &&
            command.EstimatedPreparationMinutes > _workflow.DelayApprovalThresholdMinutes)
        {
            command.NewStatus = OrderStatus.PendingApproval;
        }
    }

    private static ApiResponse<OrderDto>? ValidateOrderStatusUpdate(
        Order order, UpdateOrderStatusCommand command)
    {
        if (command.ExpectedVersion.HasValue && order.Version != command.ExpectedVersion.Value)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "The order changed. Refresh it before updating its status.",
                ErrorCodes.OrderVersionConflict);
        }

        if (!order.IsKitchenReleased)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "Release this held order through the staff release operation before changing its status.",
                ErrorCodes.KitchenReleaseRequired);
        }

        if (!OrderStatusTransitions.IsValid(order.Status, command.NewStatus))
        {
            return ApiResponse<OrderDto>.Failure($"Cannot transition from {order.Status} to {command.NewStatus}");
        }

        if (command.NewStatus == OrderStatus.Confirmed && OnlinePaymentIntent.IsAwaitingPayment(order))
        {
            return ApiResponse<OrderDto>.Failure(
                "This order is awaiting an online payment and cannot be confirmed yet.");
        }

        return null;
    }

    private OrderStatusHistory CreateStatusHistory(Order order, UpdateOrderStatusCommand command) => new()
    {
        OrderId = order.Id,
        FromStatus = order.Status,
        ToStatus = command.NewStatus,
        Notes = command.Notes,
        ChangedAt = DateTime.UtcNow,
        ChangedBy = _currentUserService.GetAuditIdentifier(),
        CreatedAt = DateTime.UtcNow,
        CreatedBy = _currentUserService.GetAuditIdentifier()
    };

    private int? ApplyStatusChange(Order order, UpdateOrderStatusCommand command)
    {
        switch (command.NewStatus)
        {
            case OrderStatus.Confirmed:
                var prepMinutes = command.EstimatedPreparationMinutes ?? _workflow.ConfirmedPreparationMinutes;
                order.EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(prepMinutes);
                return prepMinutes;

            case OrderStatus.Completed:
                order.ActualDeliveryTime = DateTime.UtcNow;
                break;

            case OrderStatus.Preparing:
                if (order.Type == OrderType.Delivery && !order.EstimatedDeliveryTime.HasValue)
                {
                    order.EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(_workflow.DeliveryPreparingMinutes);
                }
                break;

            case OrderStatus.PendingApproval:
                return command.EstimatedPreparationMinutes ?? _workflow.ConfirmedPreparationMinutes;
        }

        return null;
    }

    private async Task SendStatusNotificationAsync(
        Order order,
        OrderStatus status,
        int? preparationMinutes,
        CancellationToken cancellationToken)
    {
        if (!preparationMinutes.HasValue)
        {
            return;
        }

        if (status == OrderStatus.Confirmed)
        {
            await _notifications.SendOrderConfirmedAsync(order, preparationMinutes.Value, cancellationToken);
        }
        else if (status == OrderStatus.PendingApproval)
        {
            await _notifications.SendOrderDelayedAsync(order, preparationMinutes.Value, cancellationToken);
        }
    }

    private async Task NotifyStatusEventsAsync(
        OrderDto orderDto, string previousStatus, OrderStatus newStatus)
    {
        await _orderEventService.NotifyOrderStatusChanged(orderDto, previousStatus);

        if (newStatus == OrderStatus.Ready)
        {
            await _orderEventService.NotifyOrderReady(orderDto);
        }

        if (newStatus == OrderStatus.Completed)
        {
            await _orderEventService.NotifyOrderCompleted(orderDto);
        }
    }
}
