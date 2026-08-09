using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class CheckoutSettlementWriter : ICheckoutSettlementWriter
{
    /// <summary>Written to <c>OrderPayment.PaymentGateway</c>, the column that already names one.</summary>
    private const string GatewayName = "Stripe";

    private const int MinorUnitsPerMajor = 100;

    private readonly ApplicationDbContext _context;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderNotificationService _notifications;
    private readonly IOrderEventService _events;
    private readonly IOrderMappingService _mapping;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CheckoutSettlementWriter> _logger;

    public CheckoutSettlementWriter(
        ApplicationDbContext context,
        IOrderPaymentBuilder paymentBuilder,
        IOrderFidelityCoordinator fidelity,
        IOrderNotificationService notifications,
        IOrderEventService events,
        IOrderMappingService mapping,
        ICurrentUserService currentUser,
        ILogger<CheckoutSettlementWriter> logger)
    {
        _context = context;
        _paymentBuilder = paymentBuilder;
        _fidelity = fidelity;
        _notifications = notifications;
        _events = events;
        _mapping = mapping;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CheckoutSettlementDto> SettleAsync(
        OrderCheckoutSession session,
        string? paymentIntentId,
        long? amountReceivedMinor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // THE CLAIM. A conditional UPDATE, not a read-then-write: `WHERE Status = Created` is
        // evaluated by the database under a row lock, so when the return trip and the reconciler
        // arrive together, the second one blocks here, then matches zero rows and does nothing.
        //
        // It is inside the transaction, and that placement is the whole safety property. Writing
        // the Completed marker in its own statement would leave a row saying "settled" behind a
        // tender that was never minted if anything below threw — a dead run that reads as done, and
        // that no retry would ever pick up again. Rolled back, the row returns to Created and the
        // next caller settles it properly.
        var claimed = await _context.OrderCheckoutSessions
            .Where(s => s.Id == session.Id && s.Status == CheckoutSessionStatus.Created)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, CheckoutSessionStatus.Completed)
                    .SetProperty(x => x.PaymentIntentId, paymentIntentId)
                    .SetProperty(x => x.AmountReceivedMinor, amountReceivedMinor),
                cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogInformation(
                "Checkout session {SessionId} was already settled by another caller", session.SessionId);

            return await DescribeAsync(session.OrderId, cancellationToken);
        }

        var order = await LoadOrderAsync(session.OrderId, cancellationToken);

        var tender = CompleteTender(order, session, paymentIntentId, amountReceivedMinor);
        _paymentBuilder.UpdatePaymentSummary(order);

        var confirmed = ConfirmIfDeferred(order);

        await _context.SaveChangesAsync(cancellationToken);

        // Now that the tender has an id, point the session row at it. A second statement rather
        // than folding it into the claim above because a newly-minted tender has no id until the
        // save; both run inside the transaction, so the pair still lands or rolls back together.
        await _context.OrderCheckoutSessions
            .Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderPaymentId, tender.Id), cancellationToken);

        await AwardPointsAsync(order, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Settled checkout session {SessionId} for order {OrderNumber}: payment {PaymentStatus}, order {OrderStatus}",
            session.SessionId, order.OrderNumber, order.PaymentStatus, order.Status);

        // Deliberately after the commit. These are email and SSE — I/O that must neither hold a
        // database transaction open nor be able to roll the money back by failing.
        await NotifyAsync(order, confirmed, cancellationToken);

        return CheckoutSettlementDto.From(order);
    }

    private async Task<Order> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _context.Orders
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.Items)
            .FirstAsync(o => o.Id == orderId, cancellationToken);

    /// <summary>
    /// Completes the tender that order creation minted, or mints one if there is none.
    /// </summary>
    /// <remarks>
    /// Reusing the <c>Processing</c> tender is the normal path — it is the record that has been
    /// telling every other surface "money is in flight" since the order was placed. Creating one
    /// when it is absent keeps settlement independent of how the order was placed: a staff-created
    /// order, or a future caller that skipped the declared tender, still gets an accurate ledger
    /// rather than a silently unrecorded payment.
    /// </remarks>
    private OrderPayment CompleteTender(
        Order order, OrderCheckoutSession session, string? paymentIntentId, long? amountReceivedMinor)
    {
        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        var tender = order.Payments.FirstOrDefault(
            p => p.PaymentMethod == PaymentMethod.OnlinePayment && p.Status == PaymentStatus.Processing);

        if (tender is null)
        {
            tender = new OrderPayment
            {
                OrderId = order.Id,
                PaymentMethod = PaymentMethod.OnlinePayment,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = auditId,
            };

            order.Payments.Add(tender);
        }
        else
        {
            tender.UpdatedAt = now;
            tender.UpdatedBy = auditId;
        }

        // What STRIPE says it took, not what the order said it wanted. The two are asserted equal
        // before this runs, so they agree today — but if that assertion is ever relaxed, the ledger
        // must record the money that actually moved.
        tender.Amount = (amountReceivedMinor ?? session.AmountMinor) / (decimal)MinorUnitsPerMajor;
        tender.Currency = session.Currency;
        tender.TransactionId = paymentIntentId;
        tender.PaymentGateway = GatewayName;
        tender.Status = PaymentStatus.Completed;

        return tender;
    }

    /// <summary>
    /// Performs the confirm that order creation deferred, and reports whether it did.
    /// </summary>
    /// <remarks>
    /// Only Dine-in, because only Dine-in auto-confirms at creation — Takeaway and Delivery are
    /// Pending until staff confirm them whether or not they were paid online, and confirming them
    /// here would put orders in the kitchen that the restaurant never accepted.
    ///
    /// <para>
    /// The status guard is not ceremony: by the time a diner returns from Stripe a cashier may
    /// already have cancelled the order, and the reconciler can arrive later still. Asking the
    /// transition table rather than testing <c>== Pending</c> means this cannot invent a
    /// transition the lifecycle forbids.
    /// </para>
    /// </remarks>
    private bool ConfirmIfDeferred(Order order)
    {
        if (order.Type != OrderType.DineIn ||
            !OrderStatusTransitions.IsValid(order.Status, OrderStatus.Confirmed))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.Status,
            ToStatus = OrderStatus.Confirmed,
            Notes = "Online payment received (Dine-in auto-confirm)",
            ChangedAt = now,
            ChangedBy = auditId,
            CreatedAt = now,
            CreatedBy = auditId,
        });

        order.Status = OrderStatus.Confirmed;
        order.UpdatedAt = now;
        order.UpdatedBy = auditId;

        return true;
    }

    /// <remarks>
    /// The already-awarded check mirrors <c>AddPaymentToOrderCommandHandler</c>'s. The claim above
    /// makes settlement itself run once, but it does not stop a cashier having taken the money at
    /// the till first — that path awards the points and leaves the order Completed, so settling a
    /// Stripe payment on top of it would award them a second time.
    /// </remarks>
    private async Task AwardPointsAsync(Order order, CancellationToken cancellationToken)
    {
        if (!order.UserId.HasValue)
        {
            return;
        }

        var alreadyAwarded = await _context.FidelityPointsTransactions.AnyAsync(
            t => t.OrderId == order.Id && t.TransactionType == TransactionType.Earned, cancellationToken);

        if (alreadyAwarded)
        {
            return;
        }

        await _fidelity.AwardEarnedPointsAsync(order, order.UserId, cancellationToken);
    }

    private async Task NotifyAsync(Order order, bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            return;
        }

        var dto = await _mapping.MapToOrderDtoAsync(order, cancellationToken);

        try
        {
            await _events.NotifyOrderStatusChanged(dto, nameof(OrderStatus.Pending));
        }
        catch (Exception ex)
        {
            // The money is committed; a broadcast failure must not surface as a failed payment.
            _logger.LogError(
                ex, "Failed to broadcast confirmation for order {OrderNumber}", order.OrderNumber);
        }

        await _notifications.SendOrderConfirmedAsync(
            order, OrderNotificationService.DefaultDineInPreparationMinutes, cancellationToken);
    }

    private async Task<CheckoutSettlementDto> DescribeAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, cancellationToken);

        return CheckoutSettlementDto.From(order);
    }
}
