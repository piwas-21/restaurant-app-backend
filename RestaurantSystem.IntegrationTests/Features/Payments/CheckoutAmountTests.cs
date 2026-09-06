using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S4 (SOFRA-PAYMENTS-PLAN §5). The conversion from an order total to something Stripe will accept,
/// and the two ways it can legitimately refuse (an unusable currency, a non-positive total — the
/// third, the TWINT ceiling, was removed by #343).
///
/// <para>
/// Pure unit tests: this is arithmetic and a policy, and neither needs a database. The reason it is
/// worth pinning at all is that every failure here is silent in the wrong direction — a mis-scaled
/// amount charges 100× or 1/100× the price, and both look like a working checkout.
/// </para>
/// </summary>
public class CheckoutAmountTests
{
    [Theory]
    [InlineData(12.99, 1299)]
    [InlineData(0.01, 1)]
    [InlineData(40.00, 4000)]
    public void A_total_becomes_minor_units(decimal total, long expected)
    {
        CheckoutAmount.From(total, "CHF").Minor.Should().Be(expected);
    }

    /// <summary>
    /// The rounding line, pinned on its own. <c>order.Total</c> is <c>decimal(10,2)</c> so a third
    /// decimal should never arrive — but "should never" is exactly what the rounding call is there
    /// for, and without a case carrying real sub-cent precision that call is unexercised: dropping
    /// it, or switching it to banker's rounding, leaves the rest of this file green.
    /// </summary>
    /// <remarks>
    /// Written as a Fact with <c>decimal</c> literals rather than a Theory, because xUnit's
    /// <c>InlineData</c> stores <c>10.005</c> as a DOUBLE and converts on the way in — so the case
    /// would be testing the double→decimal conversion, not the rounding under test.
    /// </remarks>
    [Fact]
    public void Sub_cent_precision_rounds_away_from_zero()
    {
        CheckoutAmount.From(10.005m, "CHF").Minor.Should().Be(1001);
        CheckoutAmount.From(10.004m, "CHF").Minor.Should().Be(1000);
        CheckoutAmount.From(0.015m, "CHF").Minor.Should().Be(2);
    }

    /// <summary>
    /// Stripe returns and expects lower case. Ours arrives from the tenant registry in whatever
    /// case an operator typed, and the value is stored on the session row that S5 asserts against.
    /// </summary>
    [Theory]
    [InlineData("CHF", "chf")]
    [InlineData("eur", "eur")]
    [InlineData("  Chf  ", "chf")]
    public void The_currency_is_normalised(string configured, string expected)
    {
        CheckoutAmount.From(10m, configured).Currency.Should().Be(expected);
    }

    /// <summary>
    /// The important half of the allow-list. <c>LocalizationSettings.Currency</c> calls itself
    /// "ISO-4217-ish" and was display-only until S4, so anything could be in there — and a
    /// zero-decimal (JPY) or three-decimal (BHD) currency would be mis-scaled by 100× rather than
    /// rejected. Refusing loudly is the only safe answer for a currency we have not checked.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Fr.")]
    [InlineData("CH")]
    [InlineData("CHFF")]
    [InlineData("JPY")]   // real ISO-4217, but ZERO-decimal — ×100 would charge 100× the price
    [InlineData("BHD")]   // real ISO-4217, but THREE-decimal
    public void An_unusable_currency_is_refused(string? configured)
    {
        var act = () => CheckoutAmount.From(10m, configured);

        act.Should().Throw<BadRequestException>();
    }

    /// <summary>Every currency Sofra can currently onboard a tenant in (plan §3) must work.</summary>
    [Theory]
    [InlineData("CHF")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("AED")]
    public void Every_onboardable_currency_is_supported(string configured)
    {
        CheckoutAmount.From(10m, configured).Minor.Should().Be(1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nothing_to_collect_is_refused(decimal total)
    {
        var act = () => CheckoutAmount.From(total, "CHF");

        act.Should().Throw<BadRequestException>();
    }

    /// <summary>
    /// #343 regression guard. The TWINT-ceiling pre-check (max CHF 5,000) was removed: with
    /// payment methods chosen dynamically — how S4 ships them, <c>payment_method_types</c> unset —
    /// Stripe silently drops TWINT from the offered set above its ceiling and keeps card, so the
    /// pre-check refused payments Stripe would have completed (measured, plan §7.5). Just above
    /// and well above the former cap must now be chargeable in CHF.
    /// </summary>
    [Fact]
    public void A_chf_order_above_the_former_twint_cap_is_accepted()
    {
        CheckoutAmount.From(5000.01m, "CHF").Minor.Should().Be(500001);
        CheckoutAmount.From(6000m, "CHF").Minor.Should().Be(600000);
    }

    /// <summary>
    /// The former cap was CHF-only, so large EUR orders always worked — that is unchanged, and
    /// still worth pinning as the currency-independence control of the test above.
    /// </summary>
    [Fact]
    public void A_large_order_in_another_currency_is_accepted()
    {
        CheckoutAmount.From(6000m, "EUR").Minor.Should().Be(600000);
    }
}
