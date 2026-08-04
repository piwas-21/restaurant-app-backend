using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderPaymentBuilder : IOrderPaymentBuilder
{
    // 1-cent tolerance for the fully-paid / overpaid threshold. Decimal
    // arithmetic shouldn't accumulate error at this scale, but the
    // upstream basket totals can be supplied by the client and may have
    // rounding drift. Tolerating 1 cent matches the original inline behaviour.
    private const decimal PaymentTolerance = 0.01m;

    private readonly ICurrentUserService _currentUserService;

    public OrderPaymentBuilder(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public void AddPayments(Order order, IReadOnlyCollection<CreateOrderPaymentDto> payments)
    {
        var auditId = _currentUserService.GetAuditIdentifier();
        var now = DateTime.UtcNow;

        foreach (var paymentDto in payments)
        {
            var payment = new OrderPayment
            {
                PaymentMethod = paymentDto.PaymentMethod,
                Amount = paymentDto.Amount,
                Status = PaymentStatus.Pending,
                TransactionId = paymentDto.TransactionId,
                ReferenceNumber = paymentDto.ReferenceNumber,
                CardLastFourDigits = paymentDto.CardLastFourDigits,
                CardType = paymentDto.CardType,
                PaymentGateway = paymentDto.PaymentGateway,
                PaymentNotes = paymentDto.PaymentNotes,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = auditId,
            };

            // Cash stays Pending until explicitly completed via
            // AddPaymentToOrder. Other methods auto-complete here —
            // payment-gateway integration would replace this with a
            // real authorisation step.
            if (payment.PaymentMethod != PaymentMethod.Cash)
            {
                payment.Status = PaymentStatus.Completed;
            }

            order.Payments.Add(payment);
        }
    }

    public void UpdatePaymentSummary(Order order)
    {
        // Pending Cash payments don't count toward TotalPaid until they're
        // explicitly completed.
        //
        // Captured-minus-refunded is the one definition of "money we hold",
        // shared with the refund and add-payment handlers. Today this method is
        // only ever called at order CREATION, where no payment can carry a
        // refund and the refund term is always zero — but it is the method
        // named for recomputing the summary, so the next caller must not find a
        // third formula here. Issue #286 was that divergence.
        var totalPaid = order.Payments
            .Where(p => p.Status.IsCaptured())
            .Sum(p => p.Amount)
            - order.Payments.Sum(p => p.RefundedAmount ?? 0);

        order.TotalPaid = totalPaid;
        order.RemainingAmount = order.Total - totalPaid;

        if (order.RemainingAmount <= PaymentTolerance)
        {
            order.PaymentStatus = order.RemainingAmount < -PaymentTolerance
                ? PaymentStatus.Overpaid
                : PaymentStatus.Completed;
        }
        else if (totalPaid > 0)
        {
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
        }
        else
        {
            order.PaymentStatus = PaymentStatus.Pending;
        }
    }
}
