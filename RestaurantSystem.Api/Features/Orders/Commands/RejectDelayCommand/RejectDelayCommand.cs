using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.RejectDelayCommand;

public record RejectDelayCommand(Guid OrderId) : ICommand<ApiResponse<OrderDto>>
{
    /// <summary>Optional detail version; legacy email links may omit it.</summary>
    public int? ExpectedVersion { get; init; }
}

public class RejectDelayCommandHandler : ICommandHandler<RejectDelayCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderEventService _orderEventService;
    private readonly IOrderMappingService _mappingService;
    private readonly ILogger<RejectDelayCommandHandler> _logger;
    private readonly IOrderPermittedActionsService? _permittedActionsService;

    public RejectDelayCommandHandler(
        ApplicationDbContext context,
        IOrderEventService orderEventService,
        IOrderMappingService mappingService,
        ILogger<RejectDelayCommandHandler> logger,
        IOrderPermittedActionsService? permittedActionsService = null)
    {
        _context = context;
        _orderEventService = orderEventService;
        _mappingService = mappingService;
        _logger = logger;
        _permittedActionsService = permittedActionsService;
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

        if (command.ExpectedVersion.HasValue && order.Version != command.ExpectedVersion.Value)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "The order changed. Refresh it before rejecting the delay.",
                ErrorCodes.OrderVersionConflict);
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

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "The order changed. Refresh it before rejecting the delay.",
                ErrorCodes.OrderVersionConflict);
        }

        // Notify Admin (via email or just status change event)
        // We can send an email to admin saying customer rejected.

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);
        if (_permittedActionsService is not null)
        {
            orderDto.PermittedActions = _permittedActionsService.GetPermittedActions(order);
        }

        await _orderEventService.NotifyOrderStatusChanged(orderDto, previousStatus);

        _logger.LogInformation("Order {OrderNumber} rejected/cancelled by customer", order.OrderNumber);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order cancelled successfully");
    }
}
