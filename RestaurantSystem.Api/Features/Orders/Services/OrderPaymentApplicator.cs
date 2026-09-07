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
    private readonly ILogger<OrderPaymentApplicator> _logger;

    public OrderPaymentApplicator(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IFidelityPointsService fidelityPointsService,
        ILogger<OrderPaymentApplicator> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _fidelityPointsService = fidelityPointsService;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Behavior moved verbatim from <c>AddPaymentToOrderCommandHandler</c> when the table-bill
    /// flow needed the same sequence, so there is exactly one money-path implementation. The
    /// two-phase save (placeholders removed first, then reload) is deliberate — a stale
    /// placeholder reference after a single save produced double-counted totals once already.
    /// </remarks>
    public async Task<PaymentApplicationResult> ApplyToOrderAsync(
        Guid orderId, OrderPaymentTender tender, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OrderNotFound);
        }

        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Completed)
        {
            return PaymentApplicationResult.NotPayable(order.Status.ToString());
        }

        // Remove any existing Pending placeholder payments from order creation
        // These are placeholder payments that should be replaced when the actual payment is added
        var pendingPlaceholders = order.Payments.Where(p => p.Status == PaymentStatus.Pending).ToList();
        foreach (var placeholder in pendingPlaceholders)
        {
            _context.OrderPayments.Remove(placeholder);
        }

        // Save the removal of placeholder payments first
        await _context.SaveChangesAsync(cancellationToken);

        // Reload order to get fresh payment collection without stale placeholder references
        order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OrderNotFound);
        }

        var payment = new OrderPayment
        {
            OrderId = order.Id,
            PaymentMethod = tender.PaymentMethod,
            Amount = tender.Amount, // Allow overpayment - it will be flagged in payment status
            Status = PaymentStatus.Pending,
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

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        // Reload order to ensure clean state and accurate payment calculations
        order = await _context.Orders
            .Include(o => o.Payments)
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .AsSplitQuery() // sibling collections on one entity: rows multiply otherwise (S8733)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OrderNotFound);
        }

        RecomputePaymentSummary(order);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Payment {PaymentId} added to order {OrderNumber} by user {UserId}",
            payment.Id, order.OrderNumber, _currentUserService.UserId);

        await AwardFidelityPointsIfCompletedAsync(order, cancellationToken);

        return PaymentApplicationResult.Applied(order);
    }

    /// <summary>
    /// Calculated from the fresh data. Captured, not Completed: an order can already carry a
    /// refunded payment when a new tender is added, and subtracting its refund from a sum it
    /// was excluded from would push TotalPaid below what the till actually holds.
    /// </summary>
    private void RecomputePaymentSummary(Order order)
    {
        var capturedPayments = order.Payments.Where(p => p.Status.IsCaptured()).Sum(p => p.Amount);
        var refundedAmounts = order.Payments.Where(p => p.RefundedAmount.HasValue).Sum(p => p.RefundedAmount ?? 0);

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
