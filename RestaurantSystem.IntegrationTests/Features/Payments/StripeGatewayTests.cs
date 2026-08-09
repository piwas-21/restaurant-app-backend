using FluentAssertions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S3 (SOFRA-PAYMENTS-PLAN §5). The property under test is not "can it talk to Stripe" — it is
/// <b>that it ships inert</b>. This code deploys to every tenant in the fleet, none of whom have
/// bought the online-payments module, and it must be incapable of reaching Stripe for all of them.
///
/// <para>
/// Pure unit tests: no DbContext, no host, no network. A gateway that needed a running host to
/// prove it stays switched off would be the wrong shape.
/// </para>
/// </summary>
public class StripeGatewayTests
{
    /// <summary>
    /// The fleet's actual state today — an absent <c>Stripe</c> configuration section binds to all
    /// defaults. If this ever goes true, every tenant just became able to call Stripe.
    /// </summary>
    [Fact]
    public void An_unconfigured_tenant_is_not_configured()
    {
        Sut(new StripeSettings()).IsConfigured.Should().BeFalse();
    }

    /// <summary>
    /// Each of the three conditions alone. Written as a theory rather than one "happy path" test
    /// because the failure that matters is a future edit relaxing the AND into an OR — e.g.
    /// treating a present key as sufficient while the module flag is off.
    /// </summary>
    [Theory]
    [InlineData(false, "rk_test_x", "acct_x")]   // module off, but keyed  // pragma: allowlist secret
    [InlineData(true, "", "acct_x")]             // enabled, no key
    [InlineData(true, "rk_test_x", "")]          // enabled and keyed, no account  // pragma: allowlist secret
    [InlineData(true, "   ", "acct_x")]          // whitespace is not a key
    public void Every_condition_is_required(bool enabled, string key, string account)
    {
        Sut(new StripeSettings { Enabled = enabled, PlatformApiKey = key, ConnectedAccountId = account })
            .IsConfigured.Should().BeFalse();
    }

    /// <summary>
    /// The control: with all three present it IS configured. Without this, deleting the whole
    /// feature would satisfy every assertion above.
    /// </summary>
    [Fact]
    public void A_fully_configured_tenant_is_configured()
    {
        var sut = Sut(Configured());

        sut.IsConfigured.Should().BeTrue();
        sut.ConnectedAccountId.Should().Be("acct_configured");
    }

    /// <summary>
    /// An unconfigured gateway REFUSES rather than handing back a client built on an empty key.
    /// The distinction is the whole point: a client with no key constructs happily and fails at the
    /// first call — which would be at checkout, in front of a diner, as a Stripe 401.
    /// </summary>
    [Fact]
    public void An_unconfigured_gateway_refuses_instead_of_returning_a_dead_client()
    {
        var sut = Sut(new StripeSettings());

        sut.Invoking(g => g.Client).Should().Throw<BadRequestException>();
        sut.Invoking(g => g.BuildRequestOptions()).Should().Throw<BadRequestException>();
    }

    /// <summary>
    /// Every request must carry the connected account, and must NOT carry an application fee.
    /// Direct charges are what keep the money off Sofra's balance and the chargeback liability off
    /// Sofra's books (plan §2) — an application fee here would silently change the commercial model.
    /// </summary>
    [Fact]
    public void Requests_are_made_on_behalf_of_the_connected_account()
    {
        var options = Sut(Configured()).BuildRequestOptions("checkout:abc:1");

        options.StripeAccount.Should().Be("acct_configured",
            "the Stripe-Account header is the supported mechanism; OAuth account tokens are deprecated");
        options.IdempotencyKey.Should().Be("checkout:abc:1");
    }

    /// <summary>
    /// Reads need no idempotency key, and sending an empty one would be worse than sending none —
    /// Stripe treats the key as the dedupe identity.
    /// </summary>
    [Fact]
    public void A_read_carries_no_idempotency_key()
    {
        Sut(Configured()).BuildRequestOptions().IdempotencyKey.Should().BeNull();
    }

    private static StripeSettings Configured() => new()
    {
        Enabled = true,
        // Not a key — a literal that only has to be non-empty. detect-secrets flags any
        // `rk_`-shaped string on principle, which is the right default; this is the documented
        // escape for a value that never authenticated anything.
        PlatformApiKey = "rk_test_configured",  // pragma: allowlist secret
        ConnectedAccountId = "acct_configured",
    };

    private static StripeGateway Sut(StripeSettings settings) => new(Options.Create(settings));
}
