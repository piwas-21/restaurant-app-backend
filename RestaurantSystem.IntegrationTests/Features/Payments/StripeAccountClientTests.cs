using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;
using Stripe;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// P7b (SOFRA-PAYMENTS-PLAN §9) — the one Stripe read behind the payments tab.
///
/// <para>
/// Pure unit tests against a mocked <c>IStripeClient</c>: no host, no database, no network. The
/// three properties are (a) an unconfigured tenant never calls Stripe at all — which is every
/// tenant in the fleet today, (b) a refusal is swallowed into <c>null</c> rather than thrown, so
/// the endpoint above it can degrade instead of 500ing, and (c) the cache actually caches, and
/// actually expires.
/// </para>
/// <para>
/// The shape of the account payload is not invented: it is what <c>piwas sandbox</c> answered for a
/// freshly created Standard account on 2026-08-18 — <c>charges_enabled: false</c>,
/// <c>details_submitted: false</c>, <c>requirements.currently_due</c> holding 14 entries and
/// <c>disabled_reason: requirements.past_due</c>.
/// </para>
/// </summary>
public class StripeAccountClientTests
{
    private const string Account = "acct_p7b";

    /// <summary>
    /// A clock the test moves. Four lines rather than a `Microsoft.Extensions.TimeProvider.Testing`
    /// reference: this is the only place in the repo that needs one, and a cache whose expiry can
    /// only be proven by sleeping for five minutes is a cache nobody proves.
    /// </summary>
    private sealed class MovableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static StripeSettings Configured() => new()
    {
        Enabled = true,
        PlatformApiKey = "rk_test_p7b",  // pragma: allowlist secret
        ConnectedAccountId = Account,
    };

    /// <summary>An account as Stripe returns it, with <paramref name="due"/> outstanding fields.</summary>
    private static Account AccountWith(bool chargesEnabled, int due) => new()
    {
        Id = Account,
        ChargesEnabled = chargesEnabled,
        Requirements = new AccountRequirements
        {
            CurrentlyDue = Enumerable.Range(0, due).Select(i => $"field.{i}").ToList(),
        },
    };

    private static (StripeAccountClient Sut, Mock<IStripeClient> Client, MovableClock Clock) Sut(
        StripeSettings settings, Account? answer = null, StripeException? throws = null)
    {
        var client = new Mock<IStripeClient>();
        var call = client.Setup(c => c.RequestAsync<Account>(
            It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()));
        if (throws is not null) call.ThrowsAsync(throws);
        else call.ReturnsAsync(answer!);

        var gateway = new StripeGateway(Options.Create(settings));
        var clock = new MovableClock();
        // The real gateway decides IsConfigured and builds the Stripe-Account header; only the
        // transport is faked, so the test exercises the same credential path production does.
        var withFakeTransport = new Mock<IStripeGateway>();
        withFakeTransport.SetupGet(g => g.IsConfigured).Returns(gateway.IsConfigured);
        withFakeTransport.SetupGet(g => g.ConnectedAccountId).Returns(gateway.ConnectedAccountId);
        withFakeTransport.Setup(g => g.BuildRequestOptions(It.IsAny<string>()))
            .Returns(() => gateway.IsConfigured ? gateway.BuildRequestOptions() : new RequestOptions());
        withFakeTransport.SetupGet(g => g.Client).Returns(client.Object);

        return (
            new StripeAccountClient(
                withFakeTransport.Object,
                Options.Create(settings),
                NullLogger<StripeAccountClient>.Instance,
                clock),
            client,
            clock);
    }

    [Fact]
    public async Task An_unconfigured_tenant_never_calls_Stripe()
    {
        // The whole fleet today. A read here would be a guaranteed 401 on every one of them, on an
        // endpoint an admin can refresh.
        var (sut, client, _) = Sut(new StripeSettings());

        (await sut.GetConnectedAccountAsync(CancellationToken.None)).Should().BeNull();

        client.Verify(c => c.RequestAsync<Account>(
            It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reports_the_COUNT_of_outstanding_requirements_and_never_the_names()
    {
        var (sut, _, _) = Sut(Configured(), AccountWith(chargesEnabled: false, due: 14));

        var account = await sut.GetConnectedAccountAsync(CancellationToken.None);

        account.Should().NotBeNull();
        account!.ChargesEnabled.Should().BeFalse();
        account.RequirementsDueCount.Should().Be(14);
        // The type itself is the guarantee: there is nowhere on it to put a field name. Asserted
        // rather than assumed, because "add the list, it is only for admins" is a one-line change
        // and the list is the restaurant's own identity data.
        typeof(StripeConnectedAccount).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Id", "ChargesEnabled", "RequirementsDueCount"]);
    }

    [Fact]
    public async Task A_refused_read_becomes_null_rather_than_a_throw()
    {
        // The soft-fail, fired rather than read. Both refusals this must survive are Stripe-side:
        // a key without `Accounts → read`, and an Access-policy block — which plan §4 measured as
        // a 401, not a 403, so neither is distinguishable from a revoked key without guessing.
        var (sut, _, _) = Sut(
            Configured(),
            throws: new StripeException(System.Net.HttpStatusCode.Unauthorized,
                new StripeError { Code = "api_key_expired" }, "nope"));

        (await sut.GetConnectedAccountAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Reads_once_inside_the_cache_window_and_again_after_it()
    {
        var (sut, client, clock) = Sut(Configured(), AccountWith(chargesEnabled: false, due: 3));

        await sut.GetConnectedAccountAsync(CancellationToken.None);
        await sut.GetConnectedAccountAsync(CancellationToken.None);
        await sut.GetConnectedAccountAsync(CancellationToken.None);

        client.Verify(c => c.RequestAsync<Account>(
            It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // …and it is a cache, not a one-shot: a restaurant that has just finished Stripe's form
        // must see the change without being told to wait.
        clock.Advance(TimeSpan.FromMinutes(6));
        await sut.GetConnectedAccountAsync(CancellationToken.None);

        client.Verify(c => c.RequestAsync<Account>(
            It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_refusal_is_cached_too_so_it_does_not_become_sustained_traffic()
    {
        // A refused permission is a STANDING condition — the key either carries `Accounts → read`
        // or it does not. Retrying it per request turns one misconfiguration into a request storm
        // against Stripe on the same key the settle path uses.
        var (sut, client, _) = Sut(
            Configured(),
            throws: new StripeException(System.Net.HttpStatusCode.Unauthorized, new StripeError(), "nope"));

        await sut.GetConnectedAccountAsync(CancellationToken.None);
        await sut.GetConnectedAccountAsync(CancellationToken.None);

        client.Verify(c => c.RequestAsync<Account>(
            It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
