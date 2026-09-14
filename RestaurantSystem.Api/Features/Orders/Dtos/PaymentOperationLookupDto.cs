namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Outcome of an idempotent payment operation lookup.</summary>
public enum PaymentOperationLookupStatus
{
    Unknown = 0,
    Committed = 1
}

/// <summary>
/// The result of reconciling one staff payment operation after an uncertain write.
/// <para>
/// <see cref="Status"/> is <c>Committed</c> only when the operation's payment is present
/// on the requested order. An unknown operation is a successful lookup with no payment;
/// <see cref="Order"/> still carries the latest authoritative order when that order exists.
/// </para>
/// </summary>
public record PaymentOperationLookupDto
{
    /// <summary>The operation id supplied in the lookup route.</summary>
    public Guid OperationId { get; init; }

    /// <summary><c>Committed</c> or <c>Unknown</c>; this is not a payment-status enum.</summary>
    public PaymentOperationLookupStatus Status { get; init; }

    /// <summary>The original persisted tender, when this operation committed.</summary>
    public OrderPaymentDto? Payment { get; init; }

    /// <summary>
    /// The current order assembled by the normal order mapper. It is included for both states
    /// when the requested order exists, so the caller never replaces local state with a stale
    /// POST response after reconciliation.
    /// </summary>
    public OrderDto? Order { get; init; }
}
