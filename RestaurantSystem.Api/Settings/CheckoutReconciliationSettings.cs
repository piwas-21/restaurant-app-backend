namespace RestaurantSystem.Api.Settings;

/// <summary>
/// The polling reconciler behind Stripe hosted Checkout (SOFRA-PAYMENTS-PLAN S7). There is no
/// webhook in v1 — the platform may not register one on a connected account — so this is the only
/// thing that ever learns about a diner who paid and closed the tab, or who walked away.
/// </summary>
public class CheckoutReconciliationSettings
{
    public const string SectionName = "CheckoutReconciliation";

    /// <summary>
    /// Master switch, and it is a <b>data-loss</b> switch (CLAUDE.md §9): this sweep cancels orders.
    /// Off by default so the capability deploys inert to the whole fleet, exactly as the Stripe
    /// integration it backs does.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How often to sweep. Seconds rather than hours, unlike the retention sweepers: a diner who
    /// paid and closed the tab is only confirmed by this poll, so the interval is how late their
    /// order can be. Clamped to a floor at startup so a 0 cannot tight-loop Stripe.
    /// </summary>
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Sessions examined per sweep, per population. A bound rather than a target — it exists so the
    /// first sweep after an outage cannot issue thousands of Stripe calls in one pass; the backlog
    /// drains over the following sweeps instead.
    /// </summary>
    public int BatchSize { get; set; } = 100;
}
