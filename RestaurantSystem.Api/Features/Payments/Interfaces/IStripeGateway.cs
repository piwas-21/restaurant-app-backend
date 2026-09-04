using Stripe;

namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// The single seam between this codebase and Stripe. Everything that talks to Stripe goes through
/// here so that "is online payment usable at all?" is one question with one answer.
///
/// <para>
/// <b>Inert by default.</b> <see cref="IsConfigured"/> is false unless the module is enabled AND a
/// platform key AND a connected account are all present, which is the state of every tenant today.
/// Callers must check it; the request builders throw rather than silently talking to Stripe with an
/// empty key, because a 401 from Stripe at checkout time is a worse failure than a refusal here.
/// </para>
/// </summary>
public interface IStripeGateway
{
    /// <summary>
    /// True only when this tenant can actually transact: enabled, keyed, and bound to an account.
    /// The availability endpoint (S8) reports this, ANDed with the module flag.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>The connected account every request is made on behalf of.</summary>
    string ConnectedAccountId { get; }

    /// <summary>
    /// Per-request options carrying the <c>Stripe-Account</c> header — the supported way to act on
    /// a connected account now that OAuth's account-scoped token is deprecated. This is still a
    /// direct charge — the money never touches Sofra's balance, Stripe transfers the fee to the
    /// platform out of the connected account after the charge settles — and Sofra takes no share by
    /// default; when a tenant is on a commission rate, <c>StripeCheckoutClient</c> is the one that
    /// sets <c>application_fee_amount</c> per session, not this gateway.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Replayed verbatim on retry so Stripe dedupes for us. Optional because reads do not need one.
    /// </param>
    RequestOptions BuildRequestOptions(string? idempotencyKey = null);

    /// <summary>The configured client, for the typed services (<c>SessionService</c> and friends).</summary>
    IStripeClient Client { get; }
}
