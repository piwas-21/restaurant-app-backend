using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
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
    private readonly ApplicationDbContext _context;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly ISettlementNotifier _notifier;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CheckoutSettlementWriter> _logger;

    public CheckoutSettlementWriter(
        ApplicationDbContext context,
        IOrderPaymentBuilder paymentBuilder,
        IOrderFidelityCoordinator fidelity,
        ISettlementNotifier notifier,
        ICurrentUserService currentUser,
        ILogger<CheckoutSettlementWriter> logger)
    {
        _context = context;
        _paymentBuilder = paymentBuilder;
        _fidelity = fidelity;
        _notifier = notifier;
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
        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        var claimed = await _context.OrderCheckoutSessions
            .Where(s => s.Id == session.Id && s.Status == CheckoutSessionStatus.Created)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, CheckoutSessionStatus.Completed)
                    .SetProperty(x => x.PaymentIntentId, paymentIntentId)
                    .SetProperty(x => x.AmountReceivedMinor, amountReceivedMinor)
                    // ExecuteUpdate never reaches ApplicationDbContext's IAuditable stamper, so
                    // these are set by hand. WHEN a session settled and WHICH caller settled it are
                    // exactly what support and the reconciler need from the money claim ticket.
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.UpdatedBy, auditId),
                cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogInformation(
                "Checkout session {SessionId} was already settled by another caller", session.SessionId);

            return await DescribeAsync(session.OrderId, cancellationToken);
        }

        var order = await LoadOrderAsync(session.OrderId, cancellationToken);

        // The order was purged while the diner was at Stripe. Money HAS moved, so this needs a human
        // either way; what it must not do is throw InvalidOperationException from FirstAsync (§5.4)
        // and hand a diner a 500 on every retry forever.
        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                "Checkout session {SessionId} settled at Stripe but order {OrderId} no longer exists — "
                + "the payment must be reconciled by hand", session.SessionId, session.OrderId);

            throw new NotFoundException("Order not found");
        }

        // Captured BEFORE the confirm below moves it. Dine-in also reaches Confirmed from
        // PendingApproval, so hard-coding Pending here would broadcast a transition that never
        // happened — the StatusHistory row would say one thing and the SSE payload another.
        var previousStatus = order.Status;

        var tender = OnlineTenderCompletion.Apply(
            order, session, paymentIntentId, amountReceivedMinor, auditId, now);
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

        // Deliberately after the commit. Email and SSE are I/O that must neither hold a database
        // transaction open nor be able to roll the money back by failing.
        if (confirmed)
        {
            await _notifier.NotifyConfirmedAsync(order, previousStatus, cancellationToken);
        }

        return CheckoutSettlementDto.From(order);
    }

    private async Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _context.Orders
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.Items)
            // Three collections in one query is a Cartesian product — items × status history ×
            // payments — and this runs on every settle. Split, so each collection is its own SELECT.
            // Safe here in a way it is not everywhere: the whole method runs inside the settle
            // transaction, so the several reads still see one consistent snapshot.
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);


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

        // Never on an order that is over. A cashier can cancel while the diner is still at Stripe,
        // and AwardEarnedPointsAsync gates only on order.PaymentStatus — which settlement has just
        // set to Completed — so without this a cancelled order earns points, and refunding it does
        // not take them back. Asked of the transition table, the same way EnsurePayable asks "is
        // this order finished", rather than listing terminal statuses a second time.
        if (!OrderStatusTransitions.IsValid(order.Status, OrderStatus.Cancelled))
        {
            _logger.LogWarning(
                "Settled a payment on order {OrderNumber}, which is already {Status} — no points awarded",
                order.OrderNumber, order.Status);
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

    private async Task<CheckoutSettlementDto> DescribeAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order not found");

        return CheckoutSettlementDto.From(order);
    }
}
