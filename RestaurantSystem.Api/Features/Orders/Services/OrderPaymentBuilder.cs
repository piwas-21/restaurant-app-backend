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
    // the till, where a human counts it. OnlinePayment is safe because it settles at
    // STRIPE: its tender is created Processing, contributes nothing to TotalPaid, and
    // only the settle path — which re-fetches from Stripe before it writes — may
    // complete it. Declaring one buys a caller nothing except a slower order.
    private static readonly HashSet<PaymentMethod> SelfServiceMethods =
        [PaymentMethod.Cash, PaymentMethod.OnlinePayment];

    public void AddPayments(Order order, IReadOnlyCollection<CreateOrderPaymentDto> payments)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(payments);

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

            var isOnline = paymentDto.PaymentMethod == PaymentMethod.OnlinePayment;

            var payment = new OrderPayment
            {
                PaymentMethod = paymentDto.PaymentMethod,

                // An online tender's amount is the ORDER's, never the request's. The declared
                // amount is the last money field a caller still controls on this anonymous
                // endpoint, and a tender for less than the order would settle as PartiallyPaid
                // against a Stripe charge that took the full total. Cash keeps taking the
                // declared figure: it is a note for the cashier about what the diner intends to
                // hand over, and a human counts it either way.
                //
                // ⚠️ order.Total is not final here. Points redemption FKs the order, so it runs
                // after SaveChangesAsync and reprices downward (OrderFidelityCoordinator.RedeemAsync).
                // This figure is therefore an INTENT, and it is never what gets charged: the Stripe
                // session is minted later from the persisted total, and the settle path overwrites
                // Amount with what Stripe actually took. Nothing reads it in between — Processing
                // is not IsCaptured(), so TotalPaid stays 0 either way.
                Amount = isOnline ? order.Total : paymentDto.Amount,

                // EVERY tender starts un-captured, including the till's. Nothing at
                // order-creation time has observed money changing hands: the cashier
                // completes through AddPaymentToOrder, which is [RequireStaff] and
                // carries the transaction reference. Non-cash methods used to
                // auto-complete right here, taking TransactionId/PaymentGateway
                // verbatim from the request body — on an anonymous endpoint — so a
                // caller could hand itself a paid order. Whatever replaces this for a
                // real gateway must key off the gateway's answer, never the request.
                //
                // Processing rather than Pending for online, and the difference is
                // load-bearing twice over. AddPaymentToOrderCommandHandler DELETES every
                // Pending tender on the order it is settling, so a Pending online tender
                // would be silently swept away by a cashier taking cash at the till —
                // destroying the only local record that money is live at Stripe. And
                // UpdateOrderStatusCommand keys off Processing to refuse a premature
                // Confirm. Neither is true of Pending.
                Status = isOnline ? PaymentStatus.Processing : PaymentStatus.Pending,
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
        // all Pending or Processing until something that has seen the money completes
        // them, and neither is IsCaptured(). (This used to say "Pending Cash payments",
        // back when a non-cash tender was auto-completed here.)
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
