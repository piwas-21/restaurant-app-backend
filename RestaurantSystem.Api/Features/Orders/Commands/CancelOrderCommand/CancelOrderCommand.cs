using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
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
    private readonly IEmailLanguageResolver _languages;

    public CancelOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderMappingService mappingService,
        IEmailService emailService,
        IEmailLanguageResolver languages,
        ILogger<CancelOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
        _mappingService = mappingService;
        _emailService = emailService;
        _languages = languages;
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

        // Computed BEFORE the status-history row is built, because the row's Notes carry it: the
        // cancellation and the money still owed on it are one audit entry, not two.
        var gatewayHeld = order.Payments
            .Where(p => p.Status == PaymentStatus.Completed && !p.IsRefunded && TenderCustody.IsHeldByGateway(p))
            .ToList();

        // Add status history
        var statusHistory = new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.Status,
            ToStatus = OrderStatus.Cancelled,
            Notes = CancellationNotes.Build(command.CancellationReason, gatewayHeld),
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

        // Give back every payment the restaurant is actually holding. A gateway-held tender is
        // SKIPPED rather than booked back, because this handler moves no money — it writes down
        // that money moved, and for a Stripe capture nothing did (TenderCustody). Cancelling is
        // still allowed: the order is a real service record and staff must be able to close it.
        // What that leaves is an honest, visible debt — a cancelled order whose TotalPaid still
        // reports the charge — which is the same call CheckoutClearanceSweep makes when it corrects
        // money and refuses to touch the order. A wrong number nobody can see is the worse failure.
        foreach (var payment in order.Payments.Where(p =>
                     p.Status == PaymentStatus.Completed && !p.IsRefunded && !TenderCustody.IsHeldByGateway(p)))
        {
            payment.IsRefunded = true;
            payment.RefundedAmount = payment.Amount;
            payment.RefundDate = DateTime.UtcNow;
            payment.RefundReason = "Order cancelled";
            payment.Status = PaymentStatus.Refunded;
            payment.UpdatedAt = DateTime.UtcNow;
            payment.UpdatedBy = _currentUserService.GetAuditIdentifier();
        }

        foreach (var payment in gatewayHeld)
        {
            _logger.LogWarning(
                // One {Gateway} placeholder, not two: a repeated name in a message template binds
                // unreliably across sinks (S6677), so the sentence names the gateway once and then
                // refers back to it.
                "Order {OrderNumber} was cancelled holding {Amount} captured by {Gateway} "
                + "(transaction {TransactionId}). It was NOT booked as refunded — issue the refund "
                + "from that gateway's own dashboard",
                order.OrderNumber, payment.Amount, payment.PaymentGateway, payment.TransactionId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        // Send cancellation email to customer
        if (!string.IsNullOrEmpty(order.CustomerEmail))
        {
            try
            {
                // A cancellation is a staff action, so the order's frozen language is the guest's
                // only voice here (§6.10).
                await _emailService.SendOrderCancellationEmailAsync(
                    _languages.ForGuest(order.PreferredLanguage),
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
