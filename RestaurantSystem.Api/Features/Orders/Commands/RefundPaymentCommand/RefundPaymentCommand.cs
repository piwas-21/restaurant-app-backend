using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;

public record RefundPaymentCommand : ICommand<ApiResponse<OrderPaymentDto>>
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal RefundAmount { get; set; }
    public string RefundReason { get; set; } = null!;
}

public class RefundPaymentCommandHandler : ICommandHandler<RefundPaymentCommand, ApiResponse<OrderPaymentDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<RefundPaymentCommandHandler> _logger;

    public RefundPaymentCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<RefundPaymentCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<OrderPaymentDto>> Handle(RefundPaymentCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return ApiResponse<OrderPaymentDto>.Failure("Order not found");
        }

        var payment = order.Payments.FirstOrDefault(p => p.Id == command.PaymentId);
        if (payment == null)
        {
            return ApiResponse<OrderPaymentDto>.Failure("Payment not found");
        }

        if (payment.Status != PaymentStatus.Completed)
        {
            return ApiResponse<OrderPaymentDto>.Failure("Can only refund completed payments");
        }

        // Before anything is written. Every check below this line assumes the refund is ours to
        // make; this one asks whether the money is even here. See TenderCustody — the platform's
        // Stripe key has no refunds write by design, so "book it and move on" would put a returned
        // amount in the ledger, the Z-report and the order, against a charge still sitting at
        // Stripe. Refusing is the honest answer, and it is deliberately NOT overridable: an admin
        // who could force it through would be re-creating the same false record by hand.
        if (TenderCustody.IsHeldByGateway(payment))
        {
            _logger.LogWarning(
                "Refund refused on order {OrderId} payment {PaymentId}: captured by {Gateway} "
                + "(transaction {TransactionId}), which only that gateway can reverse",
                command.OrderId, command.PaymentId, payment.PaymentGateway, payment.TransactionId);

            return ApiResponse<OrderPaymentDto>.Failure(TenderCustody.RefusalMessage(payment));
        }

        if (payment.IsRefunded)
        {
            return ApiResponse<OrderPaymentDto>.Failure("Payment has already been refunded");
        }

        if (command.RefundAmount > payment.Amount)
        {
            return ApiResponse<OrderPaymentDto>.Failure($"Refund amount cannot exceed payment amount of {payment.Amount}");
        }

        // Everything past the custody check is a TILL refund: the money is in the restaurant's own
        // drawer or on its own terminal, a human hands it back, and this records that it happened.
        // (There used to be a "TODO: process the refund through the payment gateway" here. It was
        // not a missing feature but a wrong premise — the guard above is the answer, and calling a
        // gateway from a tenant box is exactly what §4's credential design refuses to allow.)
        payment.IsRefunded = command.RefundAmount == payment.Amount;
        payment.RefundedAmount = command.RefundAmount;
        payment.RefundDate = DateTime.UtcNow;
        payment.RefundReason = command.RefundReason;
        payment.Status = command.RefundAmount == payment.Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.UpdatedBy = _currentUserService.GetAuditIdentifier();

        // Update order payment summary. The gross sum spans every CAPTURED
        // tender, not just Completed ones — a payment we just refunded is no
        // longer Completed, and summing only Completed would subtract its
        // refund without ever having added the payment.
        order.TotalPaid = order.Payments.Where(p => p.Status.IsCaptured()).Sum(p => p.Amount)
                          - order.Payments.Where(p => p.RefundedAmount.HasValue).Sum(p => p.RefundedAmount ?? 0);
        order.RemainingAmount = order.Total - order.TotalPaid;

        // Update order payment status with tolerance for floating point precision
        const decimal tolerance = 0.01m;
        if (order.Payments.All(p => p.Status == PaymentStatus.Refunded))
        {
            order.PaymentStatus = PaymentStatus.Refunded;
        }
        else if (order.RemainingAmount > tolerance)
        {
            order.PaymentStatus = order.TotalPaid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Pending;
        }
        else if (order.RemainingAmount <= -tolerance)
        {
            order.PaymentStatus = PaymentStatus.Overpaid;
        }
        else
        {
            // Within tolerance of zero - fully paid
            order.PaymentStatus = PaymentStatus.Completed;
        }

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        var paymentDto = new OrderPaymentDto
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            PaymentMethod = payment.PaymentMethod.ToString(),
            Amount = payment.Amount,
            Status = payment.Status.ToString(),
            TransactionId = payment.TransactionId,
            ReferenceNumber = payment.ReferenceNumber,
            PaymentDate = payment.PaymentDate,
            CardLastFourDigits = payment.CardLastFourDigits,
            CardType = payment.CardType,
            PaymentGateway = payment.PaymentGateway,
            PaymentNotes = payment.PaymentNotes,
            IsRefunded = payment.IsRefunded,
            RefundedAmount = payment.RefundedAmount,
            RefundDate = payment.RefundDate,
            RefundReason = payment.RefundReason
        };

        _logger.LogInformation("Payment {PaymentId} refunded for amount {RefundAmount} by user {UserId}",
            payment.Id, command.RefundAmount, _currentUserService.UserId);

        return ApiResponse<OrderPaymentDto>.SuccessWithData(paymentDto, "Payment refunded successfully");
    }
}
