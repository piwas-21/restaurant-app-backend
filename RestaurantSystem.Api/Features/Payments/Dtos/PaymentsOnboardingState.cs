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
    ///
    /// <para>
    /// Under Connect <b>Express</b> this is usually the restaurant's own turn, not Stripe's queue.
    /// The platform creates the account and prefills it, which takes Stripe's
    /// <c>requirements.currently_due</c> list from 16 fields to 6 — date of birth, phone, and
    /// accepting Stripe's terms — and the platform cannot supply that remainder: Stripe refuses
    /// terms acceptance on behalf of an account with
    /// <c>controller[requirement_collection]=stripe</c>, "which includes Standard and Express
    /// accounts". The state is derived from <c>charges_enabled</c> either way, so nothing here
    /// moves; the copy the tab hangs on it does.
    /// </para>
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
