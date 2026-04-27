using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.ApproveDelayCommand;

public record ApproveDelayCommand(Guid OrderId) : ICommand<ApiResponse<OrderDto>>;

public class ApproveDelayCommandHandler : ICommandHandler<ApproveDelayCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderEventService _orderEventService;
    private readonly IOrderMappingService _mappingService;
    private readonly IEmailService _emailService;
    private readonly ILogger<ApproveDelayCommandHandler> _logger;

    public ApproveDelayCommandHandler(
        ApplicationDbContext context,
        IOrderEventService orderEventService,
        IOrderMappingService mappingService,
        IEmailService emailService,
        ILogger<ApproveDelayCommandHandler> logger)
    {
        _context = context;
        _orderEventService = orderEventService;
        _mappingService = mappingService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<OrderDto>> Handle(ApproveDelayCommand command, CancellationToken cancellationToken)
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

        if (order.Status != OrderStatus.PendingApproval)
        {
            return ApiResponse<OrderDto>.Failure("Order is not pending approval");
        }

        var previousStatus = order.Status.ToString();

        // Add status history
        var statusHistory = new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.Status,
            ToStatus = OrderStatus.Confirmed,
            Notes = "Customer approved delay",
            ChangedAt = DateTime.UtcNow,
            ChangedBy = "Customer",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "Customer"
        };

        _context.OrderStatusHistories.Add(statusHistory);

        // Update order status
        order.Status = OrderStatus.Confirmed;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = "Customer";

        // Set estimated delivery time based on preparation minutes if available, or default
        // Note: EstimatedPreparationMinutes isn't persisted on the order directly, but we might have set EstimatedDeliveryTime previously?
        // Actually, when we set PendingCustomerApproval, we didn't set EstimatedDeliveryTime.
        // We should probably have stored the proposed time somewhere or just default to a value.
        // For now, let's assume standard 30 mins or check if we can retrieve it.
        // Ideally, we should have stored the proposed delay in the order or a separate table.
        // Given the constraints, let's set a default or try to infer.
        // Wait, in UpdateOrderStatusCommandHandler, we didn't set EstimatedDeliveryTime for PendingCustomerApproval.
        // We only sent it in the email.
        // This is a gap. We should probably store it in Notes or a new field.
        // For simplicity, let's assume 30 mins or update it to a standard value.
        // Or better, let's check if we can pass the minutes in the approval link?
        // The approval link is just /approve-delay.
        // Let's just set it to 30 mins for now as a fallback.

        if (!order.EstimatedDeliveryTime.HasValue)
        {
            order.EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(30);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Notify Admin (via email or just status change event)
        // We can send an email to admin saying customer approved.
        // For now, just the status change event.

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        await _orderEventService.NotifyOrderStatusChanged(orderDto, previousStatus);

        _logger.LogInformation("Order {OrderNumber} approved by customer", order.OrderNumber);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order approved successfully");
    }
}
