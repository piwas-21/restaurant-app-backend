using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.RejectDelayCommand;

public record RejectDelayCommand(Guid OrderId) : ICommand<ApiResponse<OrderDto>>;

public class RejectDelayCommandHandler : ICommandHandler<RejectDelayCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderEventService _orderEventService;
    private readonly IOrderMappingService _mappingService;
    private readonly ILogger<RejectDelayCommandHandler> _logger;

    public RejectDelayCommandHandler(
        ApplicationDbContext context,
        IOrderEventService orderEventService,
        IOrderMappingService mappingService,
        ILogger<RejectDelayCommandHandler> logger)
    {
        _context = context;
        _orderEventService = orderEventService;
        _mappingService = mappingService;
        _logger = logger;
    }

    public async Task<ApiResponse<OrderDto>> Handle(RejectDelayCommand command, CancellationToken cancellationToken)
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
            ToStatus = OrderStatus.Cancelled,
            Notes = "Customer rejected delay",
            ChangedAt = DateTime.UtcNow,
            ChangedBy = "Customer",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "Customer"
        };

        _context.OrderStatusHistories.Add(statusHistory);

        // Update order status
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = "Customer";
        order.CancellationReason = "Customer rejected delay";

        await _context.SaveChangesAsync(cancellationToken);

        // Notify Admin (via email or just status change event)
        // We can send an email to admin saying customer rejected.

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        await _orderEventService.NotifyOrderStatusChanged(orderDto, previousStatus);

        _logger.LogInformation("Order {OrderNumber} rejected/cancelled by customer", order.OrderNumber);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order cancelled successfully");
    }
}
