using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;

public record CancelOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }
    public string CancellationReason { get; set; } = null!;
}

public class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CancelOrderCommandHandler> _logger;
    private readonly IOrderMappingService _mappingService;
    private readonly IEmailService _emailService;

    public CancelOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderMappingService mappingService,
        IEmailService emailService,
        ILogger<CancelOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
        _mappingService = mappingService;
        _emailService = emailService;
    }

    public async Task<ApiResponse<OrderDto>> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
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

        if (order.Status == OrderStatus.Completed)
        {
            return ApiResponse<OrderDto>.Failure("Cannot cancel a completed order");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return ApiResponse<OrderDto>.Failure("Order is already cancelled");
        }

        // Add status history
        var statusHistory = new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.Status,
            ToStatus = OrderStatus.Cancelled,
            Notes = $"Cancellation reason: {command.CancellationReason}",
            ChangedAt = DateTime.UtcNow,
            ChangedBy = _currentUserService.GetAuditIdentifier(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        _context.OrderStatusHistories.Add(statusHistory);

        // Update order
        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = command.CancellationReason;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        // Process refunds for completed payments
        foreach (var payment in order.Payments.Where(p => p.Status == PaymentStatus.Completed && !p.IsRefunded))
        {
            payment.IsRefunded = true;
            payment.RefundedAmount = payment.Amount;
            payment.RefundDate = DateTime.UtcNow;
            payment.RefundReason = "Order cancelled";
            payment.Status = PaymentStatus.Refunded;
            payment.UpdatedAt = DateTime.UtcNow;
            payment.UpdatedBy = _currentUserService.GetAuditIdentifier();
            // TODO: Process actual refund through payment gateway
        }

        await _context.SaveChangesAsync(cancellationToken);

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        // Send cancellation email to customer
        if (!string.IsNullOrEmpty(order.CustomerEmail))
        {
            try
            {
                await _emailService.SendOrderCancellationEmailAsync(
                    order.CustomerEmail,
                    order.CustomerName ?? "Customer",
                    order.OrderNumber,
                    command.CancellationReason
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancellation email for order {OrderNumber}", order.OrderNumber);
            }
        }

        _logger.LogInformation("Order {OrderNumber} cancelled by user {UserId}. Reason: {Reason}",
            order.OrderNumber, _currentUserService.UserId, command.CancellationReason);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order cancelled successfully");
    }
}
