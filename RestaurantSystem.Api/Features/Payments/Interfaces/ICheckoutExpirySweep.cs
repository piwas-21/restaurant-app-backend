namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Polls live checkout sessions: settles the ones the diner paid without coming back, and ends the
/// ones that ran out of time — cancelling the order behind them (SOFRA-PAYMENTS-PLAN S7).
/// </summary>
public interface ICheckoutExpirySweep
{
    /// <summary>Runs one pass. Returns what it did, which is what the tests assert on.</summary>
    Task<CheckoutExpirySweepReport> RunAsync(int batchSize, CancellationToken cancellationToken);
}

/// <summary>
/// What one expiry pass did. Returned rather than logged-only so tests can assert the sweep
/// converges — a second pass over the same data must report zeroes.
/// </summary>
public record CheckoutExpirySweepReport
{
    /// <summary>Live sessions the pass looked at. Not the number of Stripe reads — a row that
    /// threw is counted here too.</summary>
    public int Examined { get; init; }

    /// <summary>Sessions that turned out to be paid and were settled.</summary>
    public int Settled { get; init; }

    /// <summary>Sessions Stripe reported as expired.</summary>
    public int Expired { get; init; }

    /// <summary>Orders cancelled behind an expired session. The data-loss count.</summary>
    public int OrdersCancelled { get; init; }

    /// <summary>Sessions that threw and were skipped, leaving the rest of the pass to continue.</summary>
    public int Failures { get; init; }
}
