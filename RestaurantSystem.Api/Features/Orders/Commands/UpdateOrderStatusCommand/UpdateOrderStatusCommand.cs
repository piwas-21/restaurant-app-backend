using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public record UpdateOrderStatusCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string? Notes { get; set; }
    public int? EstimatedPreparationMinutes { get; set; }
}

public class UpdateOrderStatusCommandHandler : ICommandHandler<UpdateOrderStatusCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderEventService _orderEventService;
    private readonly ILogger<UpdateOrderStatusCommandHandler> _logger;
    private readonly IOrderMappingService _mappingService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;

    public UpdateOrderStatusCommandHandler(
          ApplicationDbContext context,
          ICurrentUserService currentUserService,
          IOrderEventService orderEventService,
          IOrderMappingService mappingService,
          IEmailService emailService,
          ILogger<UpdateOrderStatusCommandHandler> logger,
          IConfiguration configuration)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderEventService = orderEventService;
        _mappingService = mappingService;
        _emailService = emailService;
        _logger = logger;
        _configuration = configuration;
    }


    public async Task<ApiResponse<OrderDto>> Handle(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        var previousStatus = order.Status.ToString();

        // Validate status transition. The table lives in the Domain layer —
        // OrderStatusTransitions — because it is a pure rule about the enum and
        // it is what the frontend mirrors.
        if (!OrderStatusTransitions.IsValid(order.Status, command.NewStatus))
        {
            return ApiResponse<OrderDto>.Failure($"Cannot transition from {order.Status} to {command.NewStatus}");
        }

        // The one deliberate exception to keeping payment state and order state decoupled.
        //
        // An online tender sits in Processing for the ~30 minutes the diner is on Stripe's hosted
        // page, and PrinterFeedQuery puts any Confirmed order in front of the kitchen. Without this,
        // a cashier clicking "Confirm" on an order that is mid-redirect hands the kitchen an unpaid
        // ticket — undoing the exact protection that holding online orders at Pending buys. The
        // settle path is what confirms these, and it completes the tender before it does.
        if (command.NewStatus == OrderStatus.Confirmed &&
            order.Payments.Any(p => p.Status == PaymentStatus.Processing))
        {
            return ApiResponse<OrderDto>.Failure(
                "This order is awaiting an online payment and cannot be confirmed yet.");
        }

        // Add status history
        var statusHistory = new OrderStatusHistory
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

        _context.OrderStatusHistories.Add(statusHistory);

        // Update order status
        order.Status = command.NewStatus;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        // Handle specific status changes
        switch (command.NewStatus)
        {
            case OrderStatus.Confirmed:
                // Set estimated delivery/preparation time
                // Default to 20 minutes if not specified
                var prepMinutes = command.EstimatedPreparationMinutes ?? 20;
                order.EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(prepMinutes);

                // Send confirmation email
                if (!string.IsNullOrEmpty(order.CustomerEmail))
                {
                    try
                    {
                        await _emailService.SendOrderConfirmedEmailAsync(
                            order.CustomerEmail,
                            order.CustomerName ?? "Customer",
                            order.OrderNumber,
                            order.Type.ToString(),
                            prepMinutes
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send order confirmed email for order {OrderNumber}", order.OrderNumber);
                    }
                }
                break;

            case OrderStatus.Completed:
                order.ActualDeliveryTime = DateTime.UtcNow;
                break;

            case OrderStatus.Preparing:
                // Update estimated time if needed
                if (order.Type == OrderType.Delivery && !order.EstimatedDeliveryTime.HasValue)
                {
                    order.EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(45);
                }
                break;
            case OrderStatus.PendingApproval:
                // Send delayed order email with approval options
                if (!string.IsNullOrEmpty(order.CustomerEmail))
                {
                    try
                    {
                        var delayedPrepMinutes = command.EstimatedPreparationMinutes ?? 20;
                        var baseUrl = _configuration["EmailSettings:BackendBaseUrl"] ?? "http://localhost:5221";
                        var approveUrl = $"{baseUrl}/api/orders/{order.Id}/approve-delay";
                        var rejectUrl = $"{baseUrl}/api/orders/{order.Id}/reject-delay";

                        _logger.LogInformation("Sending order delay email for {OrderNumber}. BackendBaseUrl from config: {BaseUrl}",
                            order.OrderNumber, baseUrl);

                        await _emailService.SendOrderDelayedEmailAsync(
                            order.CustomerEmail,
                            order.CustomerName ?? "Customer",
                            order.OrderNumber,
                            delayedPrepMinutes,
                            approveUrl,
                            rejectUrl
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send order delayed email for order {OrderNumber}", order.OrderNumber);
                    }
                }
                break;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        await _orderEventService.NotifyOrderStatusChanged(orderDto, previousStatus);

        if (command.NewStatus == OrderStatus.Ready)
        {
            await _orderEventService.NotifyOrderReady(orderDto);
        }

        if (command.NewStatus == OrderStatus.Completed)
        {
            await _orderEventService.NotifyOrderCompleted(orderDto);
        }

        _logger.LogInformation("Order {OrderNumber} status updated from {FromStatus} to {ToStatus} by user {UserId}",
            order.OrderNumber, statusHistory.FromStatus, statusHistory.ToStatus, _currentUserService.UserId);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order status updated successfully");
    }
}
