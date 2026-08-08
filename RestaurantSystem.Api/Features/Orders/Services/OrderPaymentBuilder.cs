using RestaurantSystem.Api.Common.Exceptions;
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

    // The tenders a caller may declare when the order is created. Order creation is
    // ANONYMOUS (`POST /api/Orders` and `/from-basket` carry no [Authorize], and
    // Program.cs registers no fallback policy), so this list is the whole of what
    // stops a stranger asserting how they paid. Cash is safe because it settles at
    // the till, where a human counts it. When the online-payments module lands,
    // PaymentMethod.OnlinePayment joins this list — its tender is created Processing
    // and only the gateway callback may complete it.
    private static readonly HashSet<PaymentMethod> SelfServiceMethods = [PaymentMethod.Cash];

    public void AddPayments(Order order, IReadOnlyCollection<CreateOrderPaymentDto> payments)
    {
        var auditId = _currentUserService.GetAuditIdentifier();
        var isStaff = _currentUserService.IsStaff;
        var now = DateTime.UtcNow;

        foreach (var paymentDto in payments)
        {
            if (!isStaff && !SelfServiceMethods.Contains(paymentDto.PaymentMethod))
            {
                throw new BadRequestException(
                    $"Payment method '{paymentDto.PaymentMethod}' cannot be chosen when placing an order.");
            }

            var payment = new OrderPayment
            {
                PaymentMethod = paymentDto.PaymentMethod,
                Amount = paymentDto.Amount,
                // EVERY tender starts Pending, including the till's. Nothing at
                // order-creation time has observed money changing hands: the cashier
                // completes through AddPaymentToOrder, which is [RequireStaff] and
                // carries the transaction reference. Non-cash methods used to
                // auto-complete right here, taking TransactionId/PaymentGateway
                // verbatim from the request body — on an anonymous endpoint — so a
                // caller could hand itself a paid order. Whatever replaces this for a
                // real gateway must key off the gateway's answer, never the request.
                Status = PaymentStatus.Pending,
                PaymentNotes = paymentDto.PaymentNotes,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = auditId,
            };

            order.Payments.Add(payment);
        }
    }

    public void UpdatePaymentSummary(Order order)
    {
        // No tender created alongside the order counts toward TotalPaid — they are
        // all Pending until something that has seen the money completes them. (This
        // used to say "Pending Cash payments", back when a non-cash tender was
        // auto-completed here.)
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
