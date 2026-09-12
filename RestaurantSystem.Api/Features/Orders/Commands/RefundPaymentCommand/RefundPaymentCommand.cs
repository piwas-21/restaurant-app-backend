using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;

public record RefundPaymentCommand : ICommand<ApiResponse<OrderPaymentDto>>
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }

    /// <summary>Optional detail version; old clients may omit it.</summary>
    public int? ExpectedVersion { get; set; }

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

        if (command.ExpectedVersion.HasValue && order.Version != command.ExpectedVersion.Value)
        {
            return ApiResponse<OrderPaymentDto>.FailureWithCode(
                "The order changed. Refresh it before refunding the payment.",
                ErrorCodes.OrderVersionConflict);
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

        var validationFailure = ValidatePaymentRefund(payment, command);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        ApplyRefund(payment, command);
        UpdateOrderPaymentSummary(order);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<OrderPaymentDto>.FailureWithCode(
                "The order changed. Refresh it before refunding the payment.",
                ErrorCodes.OrderVersionConflict);
        }

        var paymentDto = CreatePaymentDto(payment);

        _logger.LogInformation("Payment {PaymentId} refunded for amount {RefundAmount} by user {UserId}",
            payment.Id, command.RefundAmount, _currentUserService.UserId);

        return ApiResponse<OrderPaymentDto>.SuccessWithData(paymentDto, "Payment refunded successfully");
    }

    private ApiResponse<OrderPaymentDto>? ValidatePaymentRefund(
        OrderPayment payment, RefundPaymentCommand command)
    {
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

        return command.RefundAmount > payment.Amount
            ? ApiResponse<OrderPaymentDto>.Failure(
                $"Refund amount cannot exceed payment amount of {payment.Amount}")
            : null;
    }

    private void ApplyRefund(OrderPayment payment, RefundPaymentCommand command)
    {
        payment.IsRefunded = command.RefundAmount == payment.Amount;
        payment.RefundedAmount = command.RefundAmount;
        payment.RefundDate = DateTime.UtcNow;
        payment.RefundReason = command.RefundReason;
        payment.Status = command.RefundAmount == payment.Amount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.UpdatedBy = _currentUserService.GetAuditIdentifier();
    }

    private void UpdateOrderPaymentSummary(Order order)
    {
        order.TotalPaid = order.Payments.Where(p => p.Status.IsCaptured()).Sum(p => p.Amount)
                          - order.Payments.Where(p => p.RefundedAmount.HasValue)
                              .Sum(p => p.RefundedAmount ?? 0);
        order.RemainingAmount = order.Total - order.TotalPaid;

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
            order.PaymentStatus = PaymentStatus.Completed;
        }

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();
    }

    private static OrderPaymentDto CreatePaymentDto(OrderPayment payment) => new()
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
}
