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
/// <c>notConfigured</c>, <c>awaitingVerification</c> or <c>configured</c> — see
/// <see cref="PaymentsOnboardingState"/>. A string rather than a bool, which is what let P7b add
/// the middle value without replacing anything a shipped client already reads.
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
/// <param name="RequirementsDue">
/// How many KYC fields Stripe is still waiting for, or null when we do not know — which covers both
/// "nothing to ask about" and "the read was refused". A COUNT and never the field list: those names
/// are the restaurant's own identity data, they are shown them by Stripe on the page where they can
/// actually act on them, and a number is enough to say "there is still a form to finish".
/// </param>
public record PaymentsOnboardingDto(
    string State, string? ConnectedAccountId, string DashboardUrl, int? RequirementsDue = null);
