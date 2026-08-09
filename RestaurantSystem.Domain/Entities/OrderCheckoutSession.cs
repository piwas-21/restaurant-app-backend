using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// One Stripe hosted-Checkout redirect for an order. This is the row the settle path claims and the
/// reconciler sweeps — there is no webhook-event table, because the platform cannot register a
/// webhook on a connected account (measured; SOFRA-PAYMENTS-PLAN §4). Settlement therefore has two
/// callers, the <c>success_url</c> return trip and a polling reconciler, and both re-fetch from
/// Stripe before writing. Stripe is the only authority; this table is our claim ticket.
/// </summary>
public class OrderCheckoutSession : Entity
{
    public Guid OrderId { get; set; }

    /// <summary>Stripe <c>cs_...</c>. UNIQUE — it is the idempotency anchor for settle-by-fetch.</summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Stripe <c>pi_...</c>, null until the customer actually pays. The same value is copied onto
    /// <c>OrderPayment.TransactionId</c> when the tender is minted, rather than adding a column
    /// there — that field already exists for exactly this.
    /// </summary>
    public string? PaymentIntentId { get; set; }

    public CheckoutSessionStatus Status { get; set; } = CheckoutSessionStatus.Created;

    /// <summary>ISO-4217, lower-case as Stripe returns it (<c>chf</c>, <c>eur</c>).</summary>
    public required string Currency { get; set; }

    /// <summary>
    /// Amount in the currency's MINOR unit, because that is what Stripe speaks and rounding a
    /// decimal at the boundary twice is how totals drift. The settle path asserts Stripe's
    /// <c>amount_total</c> equals this before writing a tender.
    /// </summary>
    public long AmountMinor { get; set; }

    /// <summary>What Stripe says was actually received. Null until settled.</summary>
    public long? AmountReceivedMinor { get; set; }

    /// <summary><c>checkout:{orderId}:{attempt}</c> — replayed on retry so Stripe dedupes for us.</summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>+30 minutes, the documented Stripe minimum, not the 24 h default.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>The restaurant's <c>acct_...</c>. Direct charges settle to their balance, never ours.</summary>
    public required string ConnectedAccountId { get; set; }

    /// <summary>Set when settlement mints the tender, so a replay can tell "already done" from "not yet".</summary>
    public Guid? OrderPaymentId { get; set; }

    /// <summary>Last failure reason, for the reconciler and for support. Never shown to a diner.</summary>
    public string? LastError { get; set; }

    public virtual Order Order { get; set; } = null!;
}
