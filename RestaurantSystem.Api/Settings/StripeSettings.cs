namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Tenant→diner Stripe Connect configuration (ADR-011 Job B). Distinct from Sofra's own Mollie
/// billing, which this never touches.
/// </summary>
public class StripeSettings
{
    public const string SectionName = "Stripe";

    /// <summary>
    /// Master switch. False on every tenant that has not bought the online-payments module, which
    /// is all of them today — so this ships inert to the whole fleet.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The PLATFORM's restricted key. Stripe deprecated Connect OAuth's account-scoped
    /// <c>access_token</c> in favour of the platform key plus a <c>Stripe-Account</c> header, so
    /// this is the only supported credential; it is narrowed by permission and by an Access policy
    /// pinning it to the box IPs rather than by being per-tenant (plan §4).
    /// </summary>
    public string PlatformApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The restaurant's own <c>acct_...</c>. Direct charges settle to their balance; funds never
    /// pass through Sofra.
    /// </summary>
    public string ConnectedAccountId { get; set; } = string.Empty;
}
