using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// The read half of idempotent tender recording (#523). Answers what a committed
/// <see cref="OrderPayment.OperationId"/> already banked, so a retried till tender can be
/// resolved from committed data alone — never by writing again.
/// </summary>
public interface IOrderPaymentReplayResolver
{
    /// <summary>
    /// Null = no committed tender carries this operation id (the normal first-submit path).
    /// Otherwise: same order, method and amount → the ORIGINAL result, replayed; same order
    /// with a different method or amount →
    /// <see cref="OrderPaymentApplicationOutcome.OperationPayloadMismatch"/>; a different
    /// order → <see cref="OrderPaymentApplicationOutcome.OperationIdReused"/>. Runs before
    /// the write path and again after a lost unique-index race, so both answers come from
    /// committed data only.
    /// </summary>
    Task<PaymentApplicationResult?> ResolveOutcomeAsync(
        Guid orderId, OrderPaymentTender tender, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up an operation without a tender payload. A lookup asks only whether the operation
    /// committed on this order; it must never invent method or amount values to feed
    /// <see cref="ResolveOutcomeAsync"/> because that would turn a read into a mismatch refusal.
    /// </summary>
    Task<PaymentOperationResolution> ResolveOperationAsync(
        Guid orderId, Guid operationId, CancellationToken cancellationToken);

    /// <summary>The full-includes order read every response and replay is assembled from.</summary>
    Task<Order?> LoadOrderForResponseAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>The order and, when present on that order, the payment carrying an operation key.</summary>
public sealed record PaymentOperationResolution(Order? Order, OrderPayment? Payment);

/// <inheritdoc />
public class OrderPaymentReplayResolver : IOrderPaymentReplayResolver
{
    private readonly ApplicationDbContext _context;

    public OrderPaymentReplayResolver(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PaymentApplicationResult?> ResolveOutcomeAsync(
        Guid orderId, OrderPaymentTender tender, CancellationToken cancellationToken)
    {
        if (!tender.OperationId.HasValue)
        {
            return null; // bill flow: no operation key, no replay — its idempotency is a follow-up
        }

        var existing = await _context.OrderPayments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OperationId == tender.OperationId.Value, cancellationToken);

        if (existing == null)
        {
            return null;
        }

        if (existing.OrderId != orderId)
        {
            return PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OperationIdReused);
        }

        if (existing.PaymentMethod != tender.PaymentMethod || existing.Amount != tender.Amount)
        {
            return PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OperationPayloadMismatch);
        }

        // The original tender stands: hand back the order it banked on, assembled the way
        // every read path assembles it. The operation succeeded once; the retry must say so.
        var order = await LoadOrderForResponseAsync(orderId, cancellationToken);
        return order == null
            ? PaymentApplicationResult.Failed(OrderPaymentApplicationOutcome.OrderNotFound)
            : PaymentApplicationResult.Replayed(order);
    }

    /// <inheritdoc />
    public async Task<PaymentOperationResolution> ResolveOperationAsync(
        Guid orderId, Guid operationId, CancellationToken cancellationToken)
    {
        // Read the same full order graph used by normal responses, then inspect only its
        // operation key. The route order id scopes the result to this tenant/order: an operation
        // committed for another order is Unknown here, rather than an operation-id oracle.
        var order = await LoadOrderForResponseAsync(orderId, cancellationToken);
        var payment = order?.Payments.SingleOrDefault(p => p.OperationId == operationId);
        return new PaymentOperationResolution(order, payment);
    }

    /// <inheritdoc />
    public async Task<Order?> LoadOrderForResponseAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _context.Orders
            .Include(o => o.Payments)
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .AsSplitQuery() // sibling collections on one entity: rows multiply otherwise (S8733)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, cancellationToken);
}
