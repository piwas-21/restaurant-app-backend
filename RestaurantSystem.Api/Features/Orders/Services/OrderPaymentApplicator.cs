using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderPaymentApplicator : IOrderPaymentApplicator
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFidelityPointsService _fidelityPointsService;
    private readonly IOrderPaymentReplayResolver _replayResolver;
    private readonly ILogger<OrderPaymentApplicator> _logger;

    public OrderPaymentApplicator(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IFidelityPointsService fidelityPointsService,
        IOrderPaymentReplayResolver replayResolver,
        ILogger<OrderPaymentApplicator> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _fidelityPointsService = fidelityPointsService;
        _replayResolver = replayResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Since #523 the whole write — placeholder removal, tender insert, summary recompute — is
    /// ONE <c>SaveChanges</c> inside one transaction (its own for the till flow, the bill
    /// flow's ambient SERIALIZABLE one when present), so a timeout can no longer leave
    /// placeholders removed with no tender banked. The summary recompute reads an explicitly
    /// projected payment set — never the tracked navigation, whose contents move under EF
    /// fixup at DetectChanges time (baf121a's double-counted totals).
    /// </remarks>
    public async Task<PaymentApplicationResult> ApplyToOrderAsync(
        Guid orderId, OrderPaymentTender tender, CancellationToken cancellationToken)
    {
        // Idempotency resolves BEFORE eligibility on purpose: the original tender may already
        // have completed the order, and its retry must see SUCCESS (the money is banked), not
        // "cannot add payment to Completed order" — the timeout-after-commit shape of #523.
        var resolved = await _replayResolver.ResolveOutcomeAsync(orderId, tender, cancellationToken);
        if (resolved is not null)
        {
            return resolved;
        }

        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OrderNotFound);
        }

        if (!OrderSettlementEligibility.CanCollect(order))
        {
            return PaymentApplicationResult.NotPayable(order.Status.ToString());
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            // The bill flow wraps N calls in ITS SERIALIZABLE transaction; the single-order
            // till flow gets one of its own here. READ COMMITTED is enough: protection against
            // a double tender comes from the unique index on OperationId, not from isolation.
            if (_context.Database.CurrentTransaction is null)
            {
                transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            }

            // Remove any existing Pending placeholder payments from order creation — the
            // placeholders stand in for money until the real tender lands. They are severed
            // from the in-memory collection as they are scheduled for deletion, and the
            // summary recompute below additionally re-projects around them, so nothing reads
            // a row this save is about to remove.
            var pendingPlaceholders = order.Payments.Where(p => p.Status == PaymentStatus.Pending).ToList();
            foreach (var placeholder in pendingPlaceholders)
            {
                order.Payments.Remove(placeholder);
                _context.OrderPayments.Remove(placeholder);
            }

            var payment = new OrderPayment
            {
                OrderId = order.Id,
                PaymentMethod = tender.PaymentMethod,
                Amount = tender.Amount, // Allow overpayment - it will be flagged in payment status
                Status = PaymentStatus.Pending,
                OperationId = tender.OperationId,
                TransactionId = tender.TransactionId,
                ReferenceNumber = tender.ReferenceNumber,
                CardLastFourDigits = tender.CardLastFourDigits,
                CardType = tender.CardType,
                PaymentNotes = tender.PaymentNotes,
                PaymentDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.OrderPayments.Add(payment);

            // Every payment is recorded as already completed: cash is taken at the till, and
            // card is captured on the terminal before the cashier enters it here. There is no
            // gateway integration, so there is nothing to branch on — the two arms of the
            // if/else this replaces were byte-identical (Sonar S3923). Re-introduce the branch
            // WITH the gateway call, not before it.
            payment.Status = PaymentStatus.Completed;

            // The summary is computed over a projection WE control: what the ledger will hold
            // after this save — tracked payments minus the severed placeholders, plus the new
            // tender exactly once. The tracked navigation cannot be trusted for this read:
            // EF relationship fixup adds the new tender to order.Payments on its own schedule
            // (DetectChanges), so an explicit Append there could land it in the collection
            // TWICE and persist TotalPaid doubled — measured, not hypothetical.
            var summaryPayments = order.Payments
                .Except(pendingPlaceholders)
                .Append(payment)
                .Distinct()
                .ToList();

            RecomputePaymentSummary(order, summaryPayments);

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogInformation("Payment {PaymentId} added to order {OrderNumber} by user {UserId}",
                payment.Id, order.OrderNumber, _currentUserService.UserId);
        }
        catch (Exception ex) when (IsUniqueOperationKeyViolation(ex))
        {
            // Two concurrent first-submits of one operation id both passed the replay check,
            // and the filtered unique index is the durable guard: the loser dies here. Roll the
            // loser's transaction back BEFORE reading the ledger — a Postgres transaction is
            // aborted once a statement has failed, so the read below must not run inside it.
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
                transaction = null;
            }

            // Bank nothing twice: answer what the winner actually recorded. If the loser
            // cannot be attributed (no committed row to read), fail loudly rather than guess.
            var winner = await _replayResolver.ResolveOutcomeAsync(orderId, tender, cancellationToken);
            if (winner is not null)
            {
                _logger.LogInformation(ex,
                    "Concurrent tender for operation {OperationId} on order {OrderId} resolved as {Outcome}",
                    tender.OperationId, orderId, winner.Outcome);
                return winner;
            }

            throw;
        }
        finally
        {
            // Dispose (commit or rollback) BEFORE the fidelity award below runs: its
            // best-effort-vs-rethrow decision keys off Database.CurrentTransaction, and a
            // committed till transaction left undisposed would read as ambient.
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        // Reload order to ensure clean state and accurate payment calculations, and so the
        // response reflects the committed rows rather than the tracked mutation.
        order = await _replayResolver.LoadOrderForResponseAsync(orderId, cancellationToken);

        if (order == null)
        {
            return PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OrderNotFound);
        }

        await AwardFidelityPointsIfCompletedAsync(order, cancellationToken);

        return PaymentApplicationResult.Applied(order);
    }

    /// <summary>
    /// True when the save died on the filtered unique index over OperationId — the durable,
    /// concurrency-safe half of idempotent tender recording. The only other unique index on
    /// this table is the Guid primary key, which the application generates.
    /// </summary>
    private static bool IsUniqueOperationKeyViolation(Exception ex) =>
        ex switch
        {
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } direct
                when direct.ConstraintName?.Contains("operation_id") == true => true,
            DbUpdateException { InnerException: PostgresException wrapped }
                when wrapped.SqlState == PostgresErrorCodes.UniqueViolation
                && wrapped.ConstraintName?.Contains("operation_id") == true => true,
            _ => false
        };

    /// <summary>
    /// Calculated from the fresh data. Captured, not Completed: an order can already carry a
    /// refunded payment when a new tender is added, and subtracting its refund from a sum it
    /// was excluded from would push TotalPaid below what the till actually holds. Reads the
    /// caller-projected payment set, never the tracked navigation — see the projection at the
    /// call site for why.
    /// </summary>
    private void RecomputePaymentSummary(Order order, IReadOnlyCollection<OrderPayment> payments)
    {
        var capturedPayments = payments.Where(p => p.Status.IsCaptured()).Sum(p => p.Amount);
        var refundedAmounts = payments.Where(p => p.RefundedAmount.HasValue).Sum(p => p.RefundedAmount ?? 0);

        order.TotalPaid = capturedPayments - refundedAmounts;
        order.RemainingAmount = order.Total - order.TotalPaid;

        // Update payment status with proper tolerance for floating point precision
        const decimal tolerance = 0.01m;

        if (order.RemainingAmount > tolerance)
        {
            // Still outstanding balance
            order.PaymentStatus = order.TotalPaid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Pending;
        }
        else if (order.RemainingAmount <= -tolerance)
        {
            // Overpaid (remaining is negative)
            order.PaymentStatus = PaymentStatus.Overpaid;
        }
        else
        {
            // Remaining is within tolerance of zero - fully paid
            order.PaymentStatus = PaymentStatus.Completed;
        }

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();
    }

    /// <summary>Check if we should award fidelity points now that payment is updated.</summary>
    private async Task AwardFidelityPointsIfCompletedAsync(Order order, CancellationToken cancellationToken)
    {
        if (!order.UserId.HasValue || order.FidelityPointsEarned <= 0 ||
            (order.PaymentStatus != PaymentStatus.Completed && order.PaymentStatus != PaymentStatus.Overpaid))
        {
            return;
        }

        // Check if points already awarded
        var alreadyAwarded = await _context.FidelityPointsTransactions
            .AnyAsync(t => t.OrderId == order.Id && t.TransactionType == TransactionType.Earned, cancellationToken);

        if (alreadyAwarded)
        {
            return;
        }

        try
        {
            await _fidelityPointsService.AwardPointsAsync(
                order.UserId.Value,
                order.Id,
                order.FidelityPointsEarned,
                order.SubTotal,
                cancellationToken);

            _logger.LogInformation(
                "Awarded {Points} fidelity points to user {UserId} for order {OrderNumber} after payment completion",
                order.FidelityPointsEarned, order.UserId, order.OrderNumber);
        }
        catch (Exception ex)
            when (PostgresConcurrencyAborts.IsMatch(ex, out _) && _context.Database.CurrentTransaction is not null)
        {
            // Concurrency-class abort inside the award must NOT be swallowed when an ambient
            // (bill-flow) transaction is open: a swallowed abort dooms that transaction, and a
            // doomed transaction's COMMIT reads as success — tenders silently rolled back, an
            // empty ledger reported as paid. Rethrow; the bill command maps it to its refusal.
            // WITHOUT an ambient transaction (the single-order till flow — READ COMMITTED) the
            // tender is already committed by the time the award runs: keep the best-effort
            // log-and-continue contract a registered-user till payment has always had.
            throw;
        }
        catch (Exception ex)
        {
            // Pre-transaction behavior, preserved: an award failure is best-effort and must not
            // block a tender that was taken and recorded.
            _logger.LogError(ex, "Failed to award fidelity points for order {OrderNumber} after payment", order.OrderNumber);
        }
    }
}
