namespace RestaurantSystem.Api.Features.Payments.Dtos;

/// <summary>The <see cref="PaymentsOnboardingDto.State"/> vocabulary. Never rename one.</summary>
public static class PaymentsOnboardingState
{
    /// <summary>
    /// The module is on but Stripe is not usable yet — no platform key, or no connected account,
    /// or the master switch is off. On the fleet as it stands this is every tenant.
    /// </summary>
    public const string NotConfigured = "notConfigured";

    /// <summary>
    /// Plumbed in, and Stripe has not finished verifying the business — <c>charges_enabled</c> is
    /// false, so no card will clear yet however correct the box is. Reported only when the account
    /// was actually READ; an unreadable account is never guessed into this state, because telling a
    /// trading restaurant that Stripe is still checking them would be a fabrication.
    /// </summary>
    public const string AwaitingVerification = "awaitingVerification";

    /// <summary>
    /// Enabled, keyed and bound to an account. Since P7b it also means either that Stripe reports
    /// <c>charges_enabled</c>, or that we could not read the account at all and are reporting
    /// exactly what P7a reported — the deliberate soft-fail. It is the weaker claim of the two, and
    /// it is the right place to land: it says "as far as we can tell you are set up", which is what
    /// configuration alone supports.
    /// </summary>
    public const string Configured = "configured";
}
