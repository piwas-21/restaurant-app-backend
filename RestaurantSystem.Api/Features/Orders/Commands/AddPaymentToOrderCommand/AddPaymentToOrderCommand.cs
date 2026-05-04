using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;

using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;

public record AddPaymentToOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string? TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; }
    public string? PaymentGateway { get; set; }
    public string? PaymentNotes { get; set; }
}

public class AddPaymentToOrderCommandHandler : ICommandHandler<AddPaymentToOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AddPaymentToOrderCommandHandler> _logger;
    private readonly IOrderMappingService _mappingService;
    private readonly IFidelityPointsService _fidelityPointsService;

    public AddPaymentToOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderMappingService mappingService,
        IFidelityPointsService fidelityPointsService,
        ILogger<AddPaymentToOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mappingService = mappingService;
        _fidelityPointsService = fidelityPointsService;
        _logger = logger;
    }

    public async Task<ApiResponse<OrderDto>> Handle(AddPaymentToOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Completed)
        {
            return ApiResponse<OrderDto>.Failure($"Cannot add payment to {order.Status} order");
        }

        // Remove any existing Pending placeholder payments from order creation
        // These are placeholder payments that should be replaced when the actual payment is added
        var pendingPlaceholders = order.Payments.Where(p => p.Status == PaymentStatus.Pending).ToList();
        foreach (var placeholder in pendingPlaceholders)
        {
            _context.OrderPayments.Remove(placeholder);
        }

        // Save the removal of placeholder payments first
        await _context.SaveChangesAsync(cancellationToken);

        // Reload order to get fresh payment collection without stale placeholder references
        order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        var payment = new OrderPayment
        {
            OrderId = order.Id,
            PaymentMethod = command.PaymentMethod,
            Amount = command.Amount, // Allow overpayment - it will be flagged in payment status
            Status = PaymentStatus.Pending,
            TransactionId = command.TransactionId,
            ReferenceNumber = command.ReferenceNumber,
            CardLastFourDigits = command.CardLastFourDigits,
            CardType = command.CardType,
            PaymentGateway = command.PaymentGateway,
            PaymentNotes = command.PaymentNotes,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        _context.OrderPayments.Add(payment);

        // Process payment (integrate with payment gateway for non-cash payments)
        if (payment.PaymentMethod != PaymentMethod.Cash)
        {
            // TODO: Integrate with payment gateway
            // For now, mark as completed
            payment.Status = PaymentStatus.Completed;
        }
        else
        {
            payment.Status = PaymentStatus.Completed;
        }

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        // Reload order to ensure clean state and accurate payment calculations
        order = await _context.Orders
            .Include(o => o.Payments)
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return ApiResponse<OrderDto>.Failure("Order not found after payment");
        }

        // Now calculate from the fresh data
        var completedPayments = order.Payments.Where(p => p.Status == PaymentStatus.Completed).Sum(p => p.Amount);
        var refundedAmounts = order.Payments.Where(p => p.RefundedAmount.HasValue).Sum(p => p.RefundedAmount ?? 0);

        order.TotalPaid = completedPayments - refundedAmounts;
        order.RemainingAmount = order.Total - order.TotalPaid;

        // Update payment status with proper tolerance for floating point precision
        const decimal tolerance = 0.01m;

        if (order.RemainingAmount > tolerance)
        {
            // Still outstanding balance
            order.PaymentStatus = order.TotalPaid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Pending;
        }
        else if (order.RemainingAmount <= -tolerance)
        {
            // Overpaid (remaining is negative)
            order.PaymentStatus = PaymentStatus.Overpaid;
        }
        else
        {
            // Remaining is within tolerance of zero - fully paid
            order.PaymentStatus = PaymentStatus.Completed;
        }

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Payment {PaymentId} added to order {OrderNumber} by user {UserId}",
            payment.Id, order.OrderNumber, _currentUserService.UserId);

        // Check if we should award fidelity points now that payment is updated
        if (order.UserId.HasValue && order.FidelityPointsEarned > 0 &&
           (order.PaymentStatus == PaymentStatus.Completed || order.PaymentStatus == PaymentStatus.Overpaid))
        {
            // Check if points already awarded
            var alreadyAwarded = await _context.FidelityPointsTransactions
                .AnyAsync(t => t.OrderId == order.Id && t.TransactionType == TransactionType.Earned, cancellationToken);

            if (!alreadyAwarded)
            {
                try
                {
                    await _fidelityPointsService.AwardPointsAsync(
                        order.UserId.Value,
                        order.Id,
                        order.FidelityPointsEarned,
                        order.SubTotal,
                        cancellationToken);

                    _logger.LogInformation("Awarded {Points} fidelity points to user {UserId} for order {OrderNumber} after payment completion",
                        order.FidelityPointsEarned, order.UserId, order.OrderNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to award fidelity points for order {OrderNumber} after payment", order.OrderNumber);
                }
            }
        }

        // Return the updated order so frontend gets current payment status
        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Payment added successfully");
    }
}
