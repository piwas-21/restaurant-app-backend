using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
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
    /// <summary>Cashier-refunded gratuity in minor currency units; omitted means zero.</summary>
    public long? RefundTipMinor { get; set; }
    [JsonIgnore]
    internal long EffectiveRefundTipMinor => RefundTipMinor ?? 0;
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
        await using var accountMutation = await OrderAccountMutationScope.BeginAsync(
            _context, command.OrderId, cancellationToken);
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return ApiResponse<OrderPaymentDto>.Failure("Order not found");
        }

        ExternalOrderLocalMutationGuard.RequireLocalOrder(order);

        if (order.ServiceSessionId is Guid serviceSessionId)
        {
            await AccountPaymentLedgerGuard.RequireLegacyCollectionAsync(
                _context, serviceSessionId, cancellationToken);
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

        await OrderAmendmentFinancialGuard.AssertNoPendingSourceResolutionAsync(
            _context, order.Id, cancellationToken);
        ApplyRefund(payment, command);
        UpdateOrderPaymentSummary(order);

        try
        {
            accountMutation.RecordAccountChange();
            await _context.SaveChangesAsync(cancellationToken);
            await accountMutation.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<OrderPaymentDto>.FailureWithCode(
                "The order changed. Refresh it before refunding the payment.",
                ErrorCodes.OrderVersionConflict);
        }

        var paymentDto = RefundPaymentDtoMapper.Map(payment);

        _logger.LogInformation(
            "Payment {PaymentId} refunded for food amount {RefundAmount} and tip minor amount {RefundTipMinor} by user {UserId}",
            payment.Id, command.RefundAmount, command.EffectiveRefundTipMinor, _currentUserService.UserId);

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

        if (command.RefundAmount < 0 || command.EffectiveRefundTipMinor < 0
            || (command.RefundAmount == 0 && command.EffectiveRefundTipMinor == 0))
        {
            return ApiResponse<OrderPaymentDto>.Failure("Enter a positive food refund or tip refund amount");
        }

        if (command.RefundAmount > payment.Amount)
        {
            return ApiResponse<OrderPaymentDto>.Failure(
                $"Refund amount cannot exceed payment amount of {payment.Amount}");
        }

        return command.EffectiveRefundTipMinor > payment.TipMinor
            ? ApiResponse<OrderPaymentDto>.Failure("Refunded tip cannot exceed the tip collected for this payment")
            : null;
    }

    private void ApplyRefund(OrderPayment payment, RefundPaymentCommand command)
    {
        payment.IsRefunded = command.RefundAmount == payment.Amount
            && command.EffectiveRefundTipMinor == payment.TipMinor;
        payment.RefundedAmount = command.RefundAmount;
        payment.RefundedTipMinor = command.EffectiveRefundTipMinor;
        payment.RefundDate = DateTime.UtcNow;
        payment.RefundReason = command.RefundReason;
        payment.Status = payment.IsRefunded
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
        order.RemainingAmount = order.PayableTotal - order.TotalPaid;

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

}
