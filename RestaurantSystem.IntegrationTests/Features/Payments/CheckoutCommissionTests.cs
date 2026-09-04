using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// Sofra's own commission on a tenant→diner charge (feat/stripe-application-fee). Pure unit tests:
/// this is arithmetic and a policy, same shape as <see cref="CheckoutAmountTests"/>, and neither
/// needs a database or a network call.
///
/// <para>
/// The property worth pinning is not just "the percentage is right" — it is that a configuration
/// mistake is refused HERE rather than reaching Stripe, which will not refuse it for us. Measured
/// 2026-09-04: a confirmed PaymentIntent for 4000 CHF-minor with a requested fee of 5000 produced an
/// ApplicationFee of <c>amount=4000</c> — Stripe silently caps an oversized fee at 100% of the
/// charge instead of rejecting it, so every throwing case below is standing in for a Stripe error
/// that would never actually happen.
/// </para>
/// </summary>
public class CheckoutCommissionTests
{
    /// <summary>
    /// The fleet default. Returning null — not sending a zero — is what keeps
    /// <c>StripeCheckoutClient</c> from setting <c>application_fee_amount</c> at all, which is the
    /// whole "ships inert" guarantee for this feature.
    /// </summary>
    [Fact]
    public void Zero_bps_returns_null()
    {
        var amount = CheckoutAmount.From(40.00m, "CHF");

        CheckoutCommission.From(amount, 0).Should().BeNull();
    }

    /// <summary>4000 minor at 150 bps (1.5%) is exactly 60 — no rounding involved.</summary>
    [Fact]
    public void A_normal_rate_computes_the_fee()
    {
        var amount = CheckoutAmount.From(40.00m, "CHF");

        CheckoutCommission.From(amount, 150).Should().Be(60);
    }

    /// <summary>
    /// 10 minor at 500 bps is 10 × 500 / 10000 = <b>exactly 0.5</b> — a true midpoint, which is the
    /// only kind of value that can tell the rounding modes apart. <c>AwayFromZero</c> gives 1;
    /// <c>ToEven</c> (C#'s DEFAULT for <c>decimal.Round</c>, and what a future edit would silently
    /// fall back to if the explicit mode were dropped) gives 0, because 0 is the even neighbour —
    /// and a 0 fee returns null here, so the two modes are distinguishable as 1 vs null.
    ///
    /// <para>
    /// This test previously used 333 minor at 150 bps = 4.995 and claimed to pin the same property.
    /// It did not: 4.995 is 0.005 from 5 and 0.995 from 4, so EVERY rounding mode returns 5. Proven
    /// by mutation — swapping in <c>ToEven</c> left the whole suite green. A midpoint is not a
    /// decorative detail in a rounding test; it is the entire test.
    /// </para>
    /// </summary>
    [Fact]
    public void Rounding_is_away_from_zero()
    {
        var amount = CheckoutAmount.From(0.10m, "CHF");

        CheckoutCommission.From(amount, 500).Should().Be(1);
    }

    /// <summary>
    /// Exactly at the ceiling is ALLOWED — the guard is <c>bps &gt; MaxBps</c>, not <c>&gt;=</c>.
    /// Without this, `Bps_above_the_ceiling_throws` alone leaves the boundary unpinned: swapping the
    /// operator to <c>&gt;=</c> left the whole suite green under mutation, so a tenant deliberately
    /// configured at the documented 10% maximum would start throwing on EVERY checkout and no test
    /// would have said so. 4000 minor at 1000 bps is 400.
    /// </summary>
    [Fact]
    public void The_ceiling_itself_is_allowed()
    {
        var amount = CheckoutAmount.From(40.00m, "CHF");

        CheckoutCommission.From(amount, 1000).Should().Be(400);
    }

    /// <summary>
    /// A negative rate cannot come from an operator typing a percentage — it is a configuration
    /// error and must be caught here, not surfaced as a confusing Stripe-side failure at checkout.
    /// </summary>
    [Fact]
    public void Negative_bps_throws()
    {
        var amount = CheckoutAmount.From(40.00m, "CHF");

        var act = () => CheckoutCommission.From(amount, -1);

        act.Should().Throw<BadRequestException>();
    }

    /// <summary>
    /// Above the 1000 bps (10%) ceiling must throw rather than reach Stripe. This is NOT a Stripe
    /// validation we are duplicating — it is compensating for the absence of one: Stripe does not
    /// reject an oversized <c>application_fee_amount</c>, it silently CAPS the fee at 100% of the
    /// charge (measured 2026-09-04, see the class doc). Without this ceiling, a misconfigured rate
    /// would not fail loudly — it would quietly take the restaurant's whole order.
    /// </summary>
    [Fact]
    public void Bps_above_the_ceiling_throws()
    {
        var amount = CheckoutAmount.From(40.00m, "CHF");

        var act = () => CheckoutCommission.From(amount, 1001);

        act.Should().Throw<BadRequestException>();
    }

    /// <summary>
    /// 10 minor at 1 bps is 10 × 1 / 10000 = 0.001, which rounds to 0. Stripe requires a POSITIVE
    /// <c>application_fee_amount</c>, so a fee that computes to 0 must return null (send nothing),
    /// never an explicit 0 that Stripe would reject.
    /// </summary>
    [Fact]
    public void A_rate_that_rounds_down_to_zero_returns_null()
    {
        var amount = CheckoutAmount.From(0.10m, "CHF");

        CheckoutCommission.From(amount, 1).Should().BeNull();
    }
}
