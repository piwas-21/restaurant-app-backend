using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderPaymentApplicator : IOrderPaymentApplicator
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFidelityPointsService _fidelityPointsService;
    private readonly IOrderPaymentReplayResolver _replayResolver;
    private readonly ILogger<OrderPaymentApplicator> _logger;
    private readonly decimal _paymentTolerance;

    public OrderPaymentApplicator(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IFidelityPointsService fidelityPointsService,
        IOrderPaymentReplayResolver replayResolver,
        ILogger<OrderPaymentApplicator> logger,
        IOptions<TableServiceSessionSettings>? settings = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _fidelityPointsService = fidelityPointsService;
        _replayResolver = replayResolver;
        _logger = logger;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
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

        if (tender.ExpectedVersion.HasValue && order.Version != tender.ExpectedVersion.Value)
        {
            return PaymentApplicationResult.VersionConflict();
        }

        if (order.Payments.Any(payment =>
                payment.PaymentMethod == PaymentMethod.OnlinePayment
                && payment.Status == PaymentStatus.Processing)
            || !OrderSettlementEligibility.CanCollect(order))
        {
            return PaymentApplicationResult.NotPayable(order.Status.ToString());
        }

        var applicationResult = await OrderPaymentApplicationPersistence.ApplyAsync(
            _context, _currentUserService, _replayResolver, order, tender, _paymentTolerance, cancellationToken);
        if (applicationResult is not null)
        {
            return applicationResult;
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
