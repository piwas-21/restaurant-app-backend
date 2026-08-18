namespace RestaurantSystem.Api.Features.Payments.Dtos;

/// <summary>
/// What state this restaurant's online-payment setup is in, for its own admin
/// (SOFRA-PAYMENTS-PLAN §9 P7a).
///
/// <para>
/// <b>Why this is not the availability DTO.</b> <c>OnlinePaymentAvailabilityDto</c> is
/// ANONYMOUS and answers with one boolean, deliberately — anything richer on it is public.
/// This endpoint is admin-only, so it may carry the two things the owner actually needs and
/// a stranger must not have: WHICH account their money settles to, and where to go and do
/// something about it. The one-boolean design of the public DTO is correct and stays
/// untouched.
/// </para>
/// </summary>
/// <param name="State">
/// <c>notConfigured</c> or <c>configured</c> — see <see cref="PaymentsOnboardingState"/>.
/// A string rather than a bool because P7b adds a third value (<c>awaitingVerification</c>)
/// and a boolean would have to be replaced rather than extended, breaking a shipped client.
/// </param>
/// <param name="ConnectedAccountId">
/// The tenant's own <c>acct_…</c>, or null when there is none. Not a secret — it is a public-side
/// identifier that appears in Stripe's own dashboard URLs — and it is the string the owner (or
/// whoever they ask for help) pastes into a support conversation.
/// </param>
/// <param name="DashboardUrl">
/// Where the restaurant manages its own account. Their dashboard, not ours: Connect Standard
/// means the money is theirs and so is the login.
/// </param>
public record PaymentsOnboardingDto(string State, string? ConnectedAccountId, string DashboardUrl);

/// <summary>The <see cref="PaymentsOnboardingDto.State"/> vocabulary. Never rename one.</summary>
public static class PaymentsOnboardingState
{
    /// <summary>
    /// The module is on but Stripe is not usable yet — no platform key, or no connected account,
    /// or the master switch is off. On the fleet as it stands this is every tenant.
    /// </summary>
    public const string NotConfigured = "notConfigured";

    /// <summary>
    /// Enabled, keyed and bound to an account: this restaurant can mint a Checkout session.
    /// <b>Not</b> the same as "Stripe has finished verifying them" — that distinction needs a
    /// call to Stripe and is P7b's whole subject.
    /// </summary>
    public const string Configured = "configured";
}
