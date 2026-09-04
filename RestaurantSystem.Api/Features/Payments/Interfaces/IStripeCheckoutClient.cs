namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// The checkout-session operations, split from <see cref="IStripeGateway"/> on purpose: the gateway
/// is the CREDENTIAL seam (which key, which connected account) and this is the OPERATION seam.
/// Keeping them apart is what lets a handler test assert "the amount came from the persisted order"
/// without a network call and without Stripe.net types leaking into the test.
///
/// <para>
/// Both members re-read Stripe rather than trusting anything local, because there is no webhook in
/// v1 (plan §4) — Stripe is the only authority on whether a session is still open and what was paid.
/// </para>
/// </summary>
public interface IStripeCheckoutClient
{
    /// <summary>Mints a hosted Checkout session on the connected account.</summary>
    Task<StripeCheckoutSession> CreateAsync(CheckoutSessionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads a session by id. Null <b>only</b> when Stripe does not know the id — which is a
    /// real state (a key or connected account swapped underneath us, a database restored across
    /// environments) and must be recoverable rather than fatal. Every other Stripe failure still
    /// throws: a 401 from a revoked key must not be indistinguishable from "no such session".
    /// </summary>
    Task<StripeCheckoutSession?> GetAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads a PaymentIntent by id, for sessions already settled. Null on the same narrow
    /// condition as <see cref="GetAsync"/> — Stripe does not know the id — and throws on everything
    /// else for the same reason.
    /// </summary>
    /// <remarks>
    /// A session's <c>complete</c> is terminal independently of whether the money cleared, so a
    /// delayed-notification method is booked as captured while its funds are still in flight
    /// (plan §6c). The session tells us nothing more after that point; the PaymentIntent is the only
    /// thing that ever reports the outcome.
    /// </remarks>
    Task<StripePaymentIntent?> GetPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken);
}

/// <summary>
/// The subset of a Stripe PaymentIntent this codebase acts on. Our own record for the same reason
/// <see cref="StripeCheckoutSession"/> is: a Stripe.net rename becomes a compile error here.
/// </summary>
public record StripePaymentIntent
{
    public required string Id { get; init; }

    /// <summary>
    /// <c>requires_payment_method</c> · <c>requires_confirmation</c> · <c>requires_action</c> ·
    /// <c>processing</c> · <c>requires_capture</c> · <c>succeeded</c> · <c>canceled</c>.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>The funds cleared. Terminal, and the only status that settles the question.</summary>
    public bool IsSucceeded => Status == "succeeded";

    /// <summary>
    /// The payment will not arrive. Tested as an EXPLICIT allow-list, never as "not succeeded":
    /// acting on this un-books money on an order the kitchen may already have cooked, so a status
    /// this code has never seen must fall through to "ask again later". <c>requires_payment_method</c>
    /// is Stripe's terminal state for a delayed method that bounced — the intent is asking for a
    /// different card, which for an abandoned Checkout session nobody will ever supply.
    /// </summary>
    public bool HasFailed => Status is "canceled" or "requires_payment_method";
}

/// <summary>
/// Everything needed to mint one session. <see cref="AmountMinor"/> is already resolved from the
/// PERSISTED order total by the caller — nothing here is request-supplied (S0b).
/// </summary>
public record CheckoutSessionRequest
{
    public required Guid OrderId { get; init; }
    public required string OrderNumber { get; init; }

    /// <summary>Lower-case ISO-4217, as Stripe speaks it.</summary>
    public required string Currency { get; init; }

    public required long AmountMinor { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// Sofra's commission for this charge, in minor units — <c>CheckoutCommission.From</c>'s
    /// result, verbatim. NOT <c>required</c>: <c>null</c> is the default and means "no commission",
    /// which is every tenant today. <see cref="Services.StripeCheckoutClient"/> only sets Stripe's
    /// <c>PaymentIntentData.ApplicationFeeAmount</c> when this is non-null, so a null value leaves
    /// the built request identical to before this property existed.
    /// </summary>
    public long? ApplicationFeeMinor { get; init; }
}

/// <summary>
/// The subset of Stripe's session this codebase acts on. Our own record rather than Stripe's
/// <c>Session</c> so the fields we depend on are enumerated in one place and a Stripe.net upgrade
/// that renames one is a compile error here, not a null at settle time.
/// </summary>
public record StripeCheckoutSession
{
    public required string Id { get; init; }

    /// <summary>The hosted page. Stripe stops returning it once the session leaves <c>open</c>.</summary>
    public string? Url { get; init; }

    /// <summary><c>open</c> · <c>complete</c> · <c>expired</c>.</summary>
    public required string Status { get; init; }

    /// <summary><c>paid</c> · <c>unpaid</c> · <c>no_payment_required</c>.</summary>
    public required string PaymentStatus { get; init; }

    public string? PaymentIntentId { get; init; }

    /// <summary>What Stripe will charge. S5 asserts this equals the amount we recorded.</summary>
    public long? AmountTotalMinor { get; init; }

    /// <summary>
    /// Still payable. The URL is part of the test rather than a separate check because Stripe stops
    /// returning one the moment a session leaves <c>open</c> — "open with no URL" is not a state a
    /// caller can do anything with.
    /// </summary>
    public bool IsOpen => Status == "open" && !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// The customer finished Checkout. Terminal, and deliberately independent of
    /// <see cref="IsPaid"/>: a delayed-notification method — SEPA, Klarna, Sofort, all reachable
    /// because we let Stripe choose methods dynamically — completes with <c>payment_status</c>
    /// still <c>unpaid</c> while the funds clear. Treating that as "not paid, so mint another
    /// session" is how a diner pays twice.
    /// </summary>
    public bool IsComplete => Status == "complete";

    /// <summary>Money has been taken. Settling on it is S5's job; S4 only refuses to mint a second.</summary>
    public bool IsPaid => PaymentStatus == "paid";

    /// <summary>
    /// Stripe says this session is over unpaid. Tested EXPLICITLY rather than as "not open and not
    /// complete", because expiry is what eventually lets the reconciler cancel the order: a status
    /// this code has never seen must fall through to "check again later", never to "give up". The
    /// conservative reading costs one more poll; the permissive one cancels a live order.
    /// </summary>
    public bool IsExpired => Status == "expired";
}
