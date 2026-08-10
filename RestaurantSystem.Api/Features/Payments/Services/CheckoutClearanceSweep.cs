using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
/// <remarks>
/// This closes the one exposure S5 shipped knowingly (plan §6c). A Checkout session is
/// <c>complete</c> the moment the diner finishes, <b>independently of whether the money cleared</b>
/// — a delayed-notification method (SEPA, Klarna, Sofort, all reachable because payment methods are
/// chosen dynamically) completes with <c>payment_status: unpaid</c> while funds settle. The settle
/// path books that as a captured tender, which is the right call: holding it at <c>Processing</c>
/// instead would wedge the order. But the settle command returns early on any non-<c>Created</c>
/// session, so nothing ever re-read Stripe for those rows — and if the payment later failed, there
/// was <b>no mechanism that could discover it</b>. This is that mechanism.
/// </remarks>
public class CheckoutClearanceSweep : ICheckoutClearanceSweep
{
    private readonly ApplicationDbContext _context;
    private readonly IStripeCheckoutClient _checkout;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CheckoutClearanceSweep> _logger;

    public CheckoutClearanceSweep(
        ApplicationDbContext context,
        IStripeCheckoutClient checkout,
        IOrderPaymentBuilder paymentBuilder,
        ICurrentUserService currentUser,
        ILogger<CheckoutClearanceSweep> logger)
    {
        _context = context;
        _checkout = checkout;
        _paymentBuilder = paymentBuilder;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CheckoutClearanceSweepReport> RunAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await _context.OrderCheckoutSessions
            .AsNoTracking()
            .Where(s => s.Status == CheckoutSessionStatus.Completed
                && s.ReconciledAt == null
                && s.PaymentIntentId != null)
            .OrderBy(s => s.CreatedAt)
            .Take(batchSize)
            .Select(s => new { s.Id, s.SessionId, s.OrderId, s.OrderPaymentId, s.PaymentIntentId })
            .ToListAsync(cancellationToken);

        var cleared = 0;
        var reversed = 0;
        var needsAttention = 0;
        var failures = 0;

        foreach (var session in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var intent = await _checkout.GetPaymentIntentAsync(session.PaymentIntentId!, cancellationToken);

                if (intent is null)
                {
                    // Only ever `resource_missing` — the client narrows to that and rethrows the
                    // rest, so a revoked key never lands here looking like a vanished payment.
                    // Nothing further is knowable, so stop asking rather than re-reading every pass.
                    needsAttention++;
                    _logger.LogError(
                        "Checkout session {SessionId} references PaymentIntent {IntentId}, which Stripe does "
                        + "not recognise for the configured account — its money must be reconciled by hand",
                        session.SessionId, session.PaymentIntentId);

                    await MarkReconciledAsync(
                        session.Id, "Stripe does not recognise this PaymentIntent for the configured account.",
                        cancellationToken);
                    continue;
                }

                if (intent.IsSucceeded)
                {
                    cleared++;
                    await MarkReconciledAsync(session.Id, error: null, cancellationToken);
                    continue;
                }

                if (!intent.HasFailed)
                {
                    // Still in flight. Leave the row unmarked so the next sweep asks again — the
                    // marker means "Stripe is definite", not "we looked once".
                    continue;
                }

                var outcome = await ReverseTenderAsync(
                    new ReversalTarget(session.Id, session.OrderId, session.OrderPaymentId, intent.Id, intent.Status),
                    cancellationToken);

                if (outcome)
                {
                    reversed++;
                }
                else
                {
                    needsAttention++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Quarantine the row, not the pass — the query is ordered oldest-first, so a row
                // that always throws would otherwise block every row behind it forever.
                failures++;
                _logger.LogError(ex, "Failed to reconcile checkout session {SessionId}", session.SessionId);
            }
        }

        if (cleared > 0 || reversed > 0 || needsAttention > 0 || failures > 0)
        {
            _logger.LogInformation(
                "Checkout clearance sweep: examined {Examined}, cleared {Cleared}, reversed {Reversed}, "
                + "{NeedsAttention} needing attention, {Failures} failure(s)",
                pending.Count, cleared, reversed, needsAttention, failures);
        }

        return new CheckoutClearanceSweepReport
        {
            Examined = pending.Count,
            Cleared = cleared,
            Reversed = reversed,
            NeedsAttention = needsAttention,
            Failures = failures,
        };
    }

    /// <summary>
    /// Un-books a tender whose funds never arrived, and leaves the order's status alone. Returns
    /// false when the row was left for a human instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The money is corrected because <c>TotalPaid</c> claiming a payment Stripe says failed is a
    /// false ledger, and every till and Z-report reads it. The <b>order</b> is deliberately not
    /// touched: by the time a delayed method fails, a dine-in order has been auto-confirmed and may
    /// be cooked and eaten. Cancelling it would destroy a real service record to tidy an accounting
    /// one. This makes the loss visible — an unpaid, confirmed order and a logged error — which is a
    /// question for a human with a phone, not for a timer. Fidelity points already awarded are left
    /// too: taking them back is <c>RefundPaymentCommand</c>'s unsolved problem, not this sweep's.
    /// </para>
    /// <para>
    /// <b>A tender that has already been refunded is never reversed.</b> A delayed debit can bounce
    /// days after staff refunded it, and <c>UpdatePaymentSummary</c> computes
    /// <c>sum(captured) − sum(refunded)</c>: flipping the tender to <c>Failed</c> drops it out of
    /// the captured sum while its <c>RefundedAmount</c> keeps being subtracted, driving
    /// <c>TotalPaid</c> NEGATIVE — the exact double-count <c>PaymentStatusExtensions</c> exists to
    /// prevent. Two things went wrong there and only a human can say which money is real.
    /// </para>
    /// </remarks>
    private async Task<bool> ReverseTenderAsync(ReversalTarget target, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == target.OrderId, cancellationToken);

        var tender = order?.Payments.FirstOrDefault(p => p.Id == target.OrderPaymentId);

        if (order is null || tender is null)
        {
            _logger.LogError(
                "Checkout session {SessionId} reported PaymentIntent {IntentId} as {Status}, but its order or "
                + "tender is gone — reconcile by hand", target.SessionId, target.IntentId, target.IntentStatus);

            await MarkReconciledAsync(
                target.SessionId,
                $"Stripe reported {target.IntentStatus}; the tender could not be found.",
                cancellationToken);
            return false;
        }

        if (tender.Status != PaymentStatus.Completed || tender.RefundedAmount is > 0)
        {
            _logger.LogError(
                "Order {OrderNumber} has a failed PaymentIntent {IntentId} ({Status}) on a tender that is "
                + "{TenderStatus} with {Refunded} refunded — NOT reversed automatically, because the money "
                + "would go negative. Reconcile by hand",
                order.OrderNumber, target.IntentId, target.IntentStatus, tender.Status, tender.RefundedAmount);

            await MarkReconciledAsync(
                target.SessionId,
                $"Stripe reported {target.IntentStatus}, but the tender is {tender.Status} and cannot be "
                + "reversed automatically.",
                cancellationToken);
            return false;
        }

        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        tender.Status = PaymentStatus.Failed;
        tender.UpdatedAt = now;
        tender.UpdatedBy = auditId;

        // Failed is not IsCaptured(), so this drops the order back out of every money total.
        _paymentBuilder.UpdatePaymentSummary(order);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogError(
            "Order {OrderNumber} was booked as paid online, but Stripe reports PaymentIntent {IntentId} as "
            + "{Status}. The tender is now Failed and the order is unpaid — it was NOT cancelled, because it "
            + "may already have been served",
            order.OrderNumber, target.IntentId, target.IntentStatus);

        await MarkReconciledAsync(
            target.SessionId,
            $"Stripe reported the PaymentIntent as {target.IntentStatus} after settlement.",
            cancellationToken);

        return true;
    }

    /// <summary>The row being reversed. A record so the reversal is not a five-argument call.</summary>
    private sealed record ReversalTarget(
        Guid SessionId, Guid OrderId, Guid? OrderPaymentId, string IntentId, string IntentStatus);

    private async Task MarkReconciledAsync(Guid sessionId, string? error, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var auditId = _currentUser.GetAuditIdentifier();

        await _context.OrderCheckoutSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.ReconciledAt, now)
                    .SetProperty(x => x.LastError, x => error ?? x.LastError)
                    // ExecuteUpdate bypasses the change tracker, so the IAuditable stamper never
                    // runs — the same hand-stamping every other write to this table does.
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.UpdatedBy, auditId),
                cancellationToken);
    }
}
