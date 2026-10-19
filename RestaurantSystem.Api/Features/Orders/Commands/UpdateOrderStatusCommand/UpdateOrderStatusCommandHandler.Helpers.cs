using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public partial class UpdateOrderStatusCommandHandler
{
    private ApiResponse<OrderDto>? ValidateOrderStatusUpdate(
        Order order, UpdateOrderStatusCommand command)
    {
        var authorization = OrderWriteAuthorizationPolicy.ForStatus(
            _currentUserService.Role, order, command.NewStatus);
        if (!authorization.Allowed)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                authorization.Message!, authorization.ErrorCode!);
        }

        if (command.ExpectedVersion.HasValue && order.Version != command.ExpectedVersion.Value)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "The order changed. Refresh it before updating its status.",
                ErrorCodes.OrderVersionConflict);
        }

        if (command.EstimatedPreparationMinutes.HasValue
            && !_currentUserService.IsAdmin
            && _currentUserService.Role != UserRole.Cashier
            && !command.TrustedPreparationOverride)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "Only a cashier or admin may set an order preparation-time override.",
                ErrorCodes.CashierRequired);
        }

        if (command.AcknowledgeCompletedWork && (command.ExpectedVersion is null
            || !_currentUserService.IsStaff || _currentUserService.IsApiToken
            || order.ExternalReference is not null
            || command.NewStatus is not (OrderStatus.Completed or OrderStatus.OutForDelivery)))
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "Refresh the order and confirm its completed kitchen work as a staff member.",
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

    private async Task<int?> ApplyStatusChangeAsync(
        Order order, UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        // Marketplace pickup/delivery timing belongs to the provider; local preparation adds no guessed ETA.
        if (order.ExternalReference is not null) return null;
        switch (command.NewStatus)
        {
            case OrderStatus.Confirmed:
                var acceptedAt = DateTime.UtcNow;
                var prepMinutes = await _preparation.ApplyAcceptedAsync(
                    order, command.EstimatedPreparationMinutes, acceptedAt, cancellationToken);
                order.EstimatedDeliveryTime = acceptedAt.AddMinutes(prepMinutes);
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
                return await _preparation.ApplyPendingApprovalAsync(
                    order, command.EstimatedPreparationMinutes, cancellationToken);
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
