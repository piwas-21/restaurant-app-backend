namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Re-reads the PaymentIntent behind sessions already settled, to discover a delayed-notification
/// payment that was booked as captured and then failed (plan §6c — before this, nothing could).
/// </summary>
public interface ICheckoutClearanceSweep
{
    /// <summary>Runs one pass. Returns what it did, which is what the tests assert on.</summary>
    Task<CheckoutClearanceSweepReport> RunAsync(int batchSize, CancellationToken cancellationToken);
}

/// <summary>
/// What one clearance pass did. Separate from the expiry sweep's report rather than a shared union
/// of both — a single record would leave each sweep always returning three zeroes, and an
/// always-zero field is indistinguishable from a broken one.
/// </summary>
public record CheckoutClearanceSweepReport
{
    /// <summary>Settled sessions the pass looked at, including any that threw.</summary>
    public int Examined { get; init; }

    /// <summary>Sessions whose funds Stripe confirmed cleared.</summary>
    public int Cleared { get; init; }

    /// <summary>Sessions whose payment turned out to have failed after capture.</summary>
    public int Reversed { get; init; }

    /// <summary>
    /// Sessions whose money needs a human — the tender was already refunded, or Stripe cannot see
    /// the intent. Deliberately not reversed automatically.
    /// </summary>
    public int NeedsAttention { get; init; }

    /// <summary>Sessions that threw and were skipped, leaving the rest of the pass to continue.</summary>
    public int Failures { get; init; }
}
