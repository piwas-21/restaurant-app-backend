namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Reads the tenant's own connected account at Stripe (SOFRA-PAYMENTS-PLAN §9 P7b).
///
/// <para>
/// A seam of its own rather than another member on <see cref="IStripeGateway"/> — the gateway is
/// the CREDENTIAL seam and this is an OPERATION seam, the same split
/// <see cref="IStripeCheckoutClient"/> already makes. It also keeps the caching in one place with
/// one reason to exist.
/// </para>
/// </summary>
public interface IStripeAccountClient
{
    /// <summary>
    /// The connected account as Stripe currently sees it, or <b>null when we could not find out</b>.
    /// </summary>
    /// <remarks>
    /// Null is not an error to the caller — it is the answer "no better information than
    /// configuration", and every caller must degrade to what it knew before rather than blanking a
    /// page. Refusals are ordinary here and there are two of them: the box key may not carry
    /// <c>Accounts → read</c> at all (§9 P0(b) is the decision to grant it), and an Access-policy
    /// block answers <b>401</b> rather than 403 (plan §4), so neither is distinguishable from a
    /// revoked key without guessing. This never throws for a Stripe-side condition.
    /// </remarks>
    Task<StripeConnectedAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The subset of a Stripe account this codebase acts on. Our own record, for the same reason
/// <see cref="StripeCheckoutSession"/> is one: a Stripe.net rename becomes a compile error here.
/// </summary>
/// <param name="Id">The <c>acct_…</c> that was read.</param>
/// <param name="ChargesEnabled">
/// Stripe's own verdict on whether this account can take a card RIGHT NOW. False for the whole KYC
/// window, which for a fresh CH account is every field at once (runbook §2b.2).
/// </param>
/// <param name="RequirementsDueCount">
/// How many of <c>requirements.currently_due</c> are outstanding — a COUNT, deliberately, never the
/// field list. Those field names are the restaurant's own KYC data (identity documents, a
/// representative's address, a tax id) and they read them on Stripe's page, from Stripe, where they
/// can act on them. A number is enough to say "there is still a form to finish"; the names would be
/// personal data crossing a boundary it has no reason to cross.
/// </param>
public record StripeConnectedAccount(string Id, bool ChargesEnabled, int RequirementsDueCount);
