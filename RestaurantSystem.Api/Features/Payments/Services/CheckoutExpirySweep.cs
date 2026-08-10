using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Commands.SettleCheckoutSessionCommand;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class CheckoutExpirySweep : ICheckoutExpirySweep
{
    private const string CancellationReason = "Online payment not completed";

    /// <summary>
    /// The tender states that mean the restaurant took the money, spelled out because
    /// <c>IsCaptured()</c> is a C# extension method and cannot be translated into the conditional
    /// UPDATE below — and that UPDATE is what makes the guard atomic rather than advisory.
    /// <c>CapturedStatusesMatchIsCaptured</c> in the tests fails if the two ever drift.
    /// </summary>
    private static readonly PaymentStatus[] CapturedStatuses =
    [
        PaymentStatus.Completed,
        PaymentStatus.PartiallyRefunded,
        PaymentStatus.Refunded,
    ];

    /// <summary>Exposed so the drift alarm can compare this list against <c>IsCaptured()</c>.</summary>
    internal static IReadOnlyList<PaymentStatus> CapturedStatusesForTests => CapturedStatuses;

    private readonly ApplicationDbContext _context;
    private readonly CustomMediator _mediator;
    private readonly IOrderMappingService _mapping;
    private readonly IOrderEventService _events;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CheckoutExpirySweep> _logger;

    public CheckoutExpirySweep(
        ApplicationDbContext context,
        CustomMediator mediator,
        IOrderMappingService mapping,
        IOrderEventService events,
        ICurrentUserService currentUser,
        ILogger<CheckoutExpirySweep> logger)
    {
        _context = context;
        _mediator = mediator;
        _mapping = mapping;
        _events = events;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// EVERY live session is polled, not only the expired ones, and that is the point rather than an
    /// oversight. A diner who paid and closed the tab is invisible to us until something asks Stripe;
    /// waiting for <c>ExpiresAt</c> would hold their confirmed order — and, for dine-in, their
    /// kitchen ticket — for the full 31 minutes. The return trip (S9) is the primary trigger and
    /// normally gets there first; this is the backstop that makes the closed tab merely slow rather
    /// than permanent.
    /// </remarks>
    public async Task<CheckoutExpirySweepReport> RunAsync(int batchSize, CancellationToken cancellationToken)
    {
        // Ordered oldest-first so a backlog drains in the order money was taken. That ordering is
        // also why a row that always throws is quarantined below rather than left to abort the pass:
        // it would be the head row every time, and nothing behind it would ever be swept again.
        var live = await _context.OrderCheckoutSessions
            .AsNoTracking()
            .Where(s => s.Status == CheckoutSessionStatus.Created)
            .OrderBy(s => s.CreatedAt)
            .Take(batchSize)
            .Select(s => new { s.Id, s.SessionId, s.OrderId })
            .ToListAsync(cancellationToken);

        var settled = 0;
        var expired = 0;
        var cancelled = 0;
        var failures = 0;

        foreach (var session in live)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outcome = await ResolveAsync(session.Id, session.SessionId, cancellationToken);

                switch (outcome)
                {
                    case CheckoutSessionStatus.Completed:
                        settled++;
                        break;

                    // ONLY an explicit expiry cancels an order. `Failed` is not "the diner did not
                    // pay" — the settle command writes it when Stripe cannot be read at all (a key
                    // or connected-account swap, a database restored across environments) and when
                    // Stripe's amount disagrees with ours, which is a session the diner DID pay and
                    // that is deliberately left visible in Stripe's dashboard for a human. Cancelling
                    // on it would mass-destroy live orders the first minute after a key mix-up.
                    case CheckoutSessionStatus.Expired:
                        expired++;
                        if (await CancelAbandonedOrderAsync(session.OrderId, cancellationToken))
                        {
                            cancelled++;
                        }

                        break;

                    case CheckoutSessionStatus.Failed:
                        _logger.LogError(
                            "Checkout session {SessionId} could not be resolved against Stripe and was failed. "
                            + "Its order is left untouched — money may have moved and needs a human",
                            session.SessionId);
                        break;

                    default:
                        // Still Created (open at Stripe), or the row vanished. Either way, nothing.
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Quarantine the row, not the pass. A revoked key, a 429, a poison row: without
                // this, one bad session at the head of the queue stops every later one — including
                // sessions a diner has already paid for — and presents only as the same log line
                // every interval.
                failures++;
                _logger.LogError(ex, "Failed to reconcile checkout session {SessionId}", session.SessionId);
            }
        }

        if (settled > 0 || expired > 0 || failures > 0)
        {
            _logger.LogInformation(
                "Checkout expiry sweep: examined {Examined}, settled {Settled}, expired {Expired}, "
                + "cancelled {Cancelled} order(s), {Failures} failure(s)",
                live.Count, settled, expired, cancelled, failures);
        }

        return new CheckoutExpirySweepReport
        {
            Examined = live.Count,
            Settled = settled,
            Expired = expired,
            OrdersCancelled = cancelled,
            Failures = failures,
        };
    }

    /// <summary>
    /// Hands the session to the settle command and reports where that left it.
    /// </summary>
    /// <remarks>
    /// The command owns every decision about the session itself — it re-reads Stripe, settles a
    /// complete one, retires an expired or unreadable one, and leaves an open one alone — and is
    /// idempotent by construction, so racing the diner's own return trip is safe. This sweep only
    /// decides what happens to the ORDER afterwards.
    ///
    /// <para>
    /// The status is projected as NULLABLE deliberately. <c>FirstOrDefaultAsync</c> over a
    /// non-nullable enum yields <c>0</c> for a missing row, and <c>0</c> is not
    /// <see cref="CheckoutSessionStatus.Created"/> (which is 1) — so a vanished row would fall out
    /// of every arm above into whichever one is last. On a §9 data-loss path the enum's default must
    /// not be able to land on the destructive side by accident.
    /// </para>
    /// </remarks>
    private async Task<CheckoutSessionStatus?> ResolveAsync(
        Guid rowId, string sessionId, CancellationToken cancellationToken)
    {
        await _mediator.SendCommand<SettleCheckoutSessionCommand, ApiResponse<CheckoutSettlementDto>>(
            new SettleCheckoutSessionCommand { SessionId = sessionId }, cancellationToken);

        return await _context.OrderCheckoutSessions
            .AsNoTracking()
            .Where(s => s.Id == rowId)
            .Select(s => (CheckoutSessionStatus?)s.Status)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Cancels the order behind a session that expired unpaid — the one destructive act in S7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only from <see cref="OrderStatus.Pending"/>, tested directly rather than through the
    /// transition table.</b> The table permits <c>Confirmed → Cancelled</c> and several more, which
    /// is right for a human with a reason and wrong for a timer: an order that reached Confirmed is
    /// on the pass and may already be cooked.
    /// </para>
    /// <para>
    /// <b>Only with no captured money</b>, because the likeliest ending for an abandoned Checkout is
    /// the diner paying cash at the till instead — and <b>both</b> that test and the status test are
    /// inside the conditional UPDATE rather than read into memory first. Every other write in this
    /// feature claims its row that way for the same reason: a cashier taking cash between a read and
    /// a save is otherwise silently overwritten, and on two app instances both would cancel and both
    /// would append a history row.
    /// </para>
    /// <para>
    /// <b>Only with no other live session</b>, since a second session means a payment may be in
    /// progress right now. That one stays a plain read: it concerns a different table, and losing
    /// the race merely costs one more sweep.
    /// </para>
    /// </remarks>
    private async Task<bool> CancelAbandonedOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var anotherSessionIsLive = await _context.OrderCheckoutSessions.AnyAsync(
            s => s.OrderId == orderId && s.Status == CheckoutSessionStatus.Created, cancellationToken);

        if (anotherSessionIsLive)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await _context.Orders
            .Where(o => o.Id == orderId
                && o.Status == OrderStatus.Pending
                && !o.Payments.Any(p => CapturedStatuses.Contains(p.Status)))
            .ExecuteUpdateAsync(
                o => o
                    .SetProperty(x => x.Status, OrderStatus.Cancelled)
                    .SetProperty(x => x.CancellationReason, CancellationReason)
                    // ExecuteUpdate bypasses the IAuditable stamper, as everywhere else here.
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.UpdatedBy, auditId),
                cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogInformation(
                "Order {OrderId} was not cancellable when its checkout session expired — it is no longer "
                + "Pending, or it has been paid by another tender", orderId);
            return false;
        }

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Cancelled,
            Notes = "Online payment was not completed before the checkout session expired",
            ChangedAt = now,
            ChangedBy = auditId,
            CreatedAt = now,
            CreatedBy = auditId,
        });

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await AnnounceAsync(orderId, cancellationToken);

        _logger.LogWarning("Cancelled order {OrderId}: its online payment was never completed", orderId);

        return true;
    }

    /// <summary>
    /// Broadcasts the cancellation on the order event stream.
    /// </summary>
    /// <remarks>
    /// Without this the cashier and kitchen views keep showing a Pending order the database says is
    /// Cancelled until somebody reloads — every other status change in the codebase publishes, and a
    /// change made by a timer is the one nobody is watching for. After the commit, and never able to
    /// fail the cancellation: the order IS cancelled by this point, and an SSE hiccup must not turn
    /// that into an exception that re-runs the whole row next sweep.
    /// </remarks>
    private async Task AnnounceAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _context.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                return;
            }

            var dto = await _mapping.MapToOrderDtoAsync(order, cancellationToken);
            await _events.NotifyOrderStatusChanged(dto, nameof(OrderStatus.Pending));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Cancelled order {OrderId} but could not announce it", orderId);
        }
    }
}
