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

    /// <summary>Re-reads a session by id. Null when Stripe does not know it.</summary>
    Task<StripeCheckoutSession?> GetAsync(string sessionId, CancellationToken cancellationToken);
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

    /// <summary>Pre-fills the Stripe receipt when the diner gave one. Guests often have not.</summary>
    public string? CustomerEmail { get; init; }
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

    /// <summary>Money has been taken. Settling on it is S5's job; S4 only refuses to mint a second.</summary>
    public bool IsPaid => PaymentStatus == "paid";
}
