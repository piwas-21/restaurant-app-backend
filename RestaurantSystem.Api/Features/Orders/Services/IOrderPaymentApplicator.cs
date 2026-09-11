using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Where an apply-payment attempt stopped. <see cref="PaymentApplicationResult.Order"/>
/// is populated on <see cref="Applied"/> and on an idempotent replay of one.</summary>
public enum OrderPaymentApplicationOutcome
{
    Applied,
    OrderNotFound,
    OrderNotInPayableStatus,

    /// <summary>The operation id already banked a tender on this order with a DIFFERENT
    /// method or amount — a retried operation may not change its own payload (#523).</summary>
    OperationPayloadMismatch,

    /// <summary>The operation id already banked a tender on a DIFFERENT order — a client
    /// bug (one Guid per cashier action), refused before the unique index has to.</summary>
    OperationIdReused,
}

public record OrderPaymentTender
{
    public PaymentMethod PaymentMethod { get; init; }
    public decimal Amount { get; init; }
    public string? TransactionId { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? CardLastFourDigits { get; init; }
    public string? CardType { get; init; }
    public string? PaymentNotes { get; init; }

    /// <summary>Client operation key for idempotent replay (#523). The till flow mints one
    /// Guid per cashier action; null on the table-bill flow, whose idempotency is a
    /// deliberate follow-up. Null never replays and never collides.</summary>
    public Guid? OperationId { get; init; }
}

public record PaymentApplicationResult(
    OrderPaymentApplicationOutcome Outcome,
    Order? Order,
    string? NotPayableStatus = null,
    bool IsIdempotentReplay = false)
{
    public static PaymentApplicationResult Applied(Order order) => new(OrderPaymentApplicationOutcome.Applied, order);

    /// <summary>The operation id already banked exactly this tender: the ORIGINAL persisted
    /// result, read back — never a second tender. Success, so the retry sees the money land.</summary>
    public static PaymentApplicationResult Replayed(Order order) =>
        new(OrderPaymentApplicationOutcome.Applied, order, IsIdempotentReplay: true);

    public static PaymentApplicationResult Failed(OrderPaymentApplicationOutcome outcome) => new(outcome, null);

    /// <summary>The order exists but is Cancelled/Completed — carries that status for the user-facing message.</summary>
    public static PaymentApplicationResult NotPayable(string status) =>
        new(OrderPaymentApplicationOutcome.OrderNotInPayableStatus, null, status);
}

/// <summary>
/// The one implementation of "take a tender for an order": replace Pending
/// placeholders, record the payment as captured, and recompute the order's
/// TotalPaid / RemainingAmount / PaymentStatus — atomically, in one save
/// inside one transaction (#523), so a timeout can no longer leave the ledger
/// half-written. Serves the single-order till flow
/// (<c>AddPaymentToOrderCommand</c>) and the table-bill flow
/// (<c>AddTableBillPaymentCommand</c>) so the two cannot drift — #286 showed
/// what payment handlers disagreeing looks like.
/// </summary>
public interface IOrderPaymentApplicator
{
    /// <summary>
    /// Idempotent per <see cref="OrderPaymentTender.OperationId"/>: a repeat of the same
    /// (order, operation id, payload) returns the original persisted result; a repeat with a
    /// different payload is refused. Null operation id (bill flow) never replays.
    /// </summary>
    Task<PaymentApplicationResult> ApplyToOrderAsync(Guid orderId, OrderPaymentTender tender, CancellationToken cancellationToken);
}
