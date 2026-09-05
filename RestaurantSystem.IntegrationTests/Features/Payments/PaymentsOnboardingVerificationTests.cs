using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// P7b (SOFRA-PAYMENTS-PLAN §9) — the third value, through the real MVC pipeline.
///
/// <para>
/// The question this answers is the one an owner actually has, and the one P7a could not: a tenant
/// is <i>configured</i> for DAYS before Stripe has finished verifying their business, and "you are
/// set up" is wrong for every one of those days. What makes it safe to ship before the box key is
/// granted <c>Accounts → read</c> (§9 P0(b)) is the SOFT-FAIL, and the soft-fail is asserted by
/// firing it — a stub that answers null, which is exactly what the real client returns on a
/// refusal — rather than by reading the code.
/// </para>
/// </summary>
public abstract class PaymentsOnboardingVerificationTestsBase : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    private TestWebApplicationFactory _factory = null!;

    protected PaymentsOnboardingVerificationTestsBase(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>What Stripe reports about the connected account, or null for "we could not find out".</summary>
    protected abstract StripeConnectedAccount? Account { get; }

    private sealed class StubAccountClient : IStripeAccountClient
    {
        private readonly StripeConnectedAccount? _account;
        public StubAccountClient(StripeConnectedAccount? account) => _account = account;
        public Task<StripeConnectedAccount?> GetConnectedAccountAsync(CancellationToken cancellationToken)
            => Task.FromResult(_account);
    }

    public Task InitializeAsync()
    {
        var account = Account;
        _factory = new TestWebApplicationFactory(
            _databaseFixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = "core,online-payments",
                ["Stripe:Enabled"] = "true",
                ["Stripe:PlatformApiKey"] = "rk_test_verification",  // pragma: allowlist secret
                ["Stripe:ConnectedAccountId"] = "acct_verification",
            },
            services =>
            {
                services.RemoveAll<IStripeAccountClient>();
                services.AddSingleton<IStripeAccountClient>(new StubAccountClient(account));
            });
        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        // The endpoint reads no database at all, so there is nothing to reset and no seed to make.
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    protected async Task<JsonElement> AskAsync()
    {
        var response = await Client.GetAsync("/api/payments/onboarding");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }
}

/// <summary>
/// A connected account Stripe will not let charge yet. The shape is the one <c>piwas sandbox</c>
/// really answered for a fresh Standard account on 2026-08-18: <c>charges_enabled: false</c> with
/// 14 outstanding requirements.
/// </summary>
[Collection("Database Lane 4")]
public class PaymentsOnboardingAwaitingVerificationTests : PaymentsOnboardingVerificationTestsBase
{
    public PaymentsOnboardingAwaitingVerificationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override StripeConnectedAccount? Account => new("acct_verification", false, 14);

    [Fact]
    public async Task Configured_but_unverified_is_its_own_state_and_carries_the_count()
    {
        var data = await AskAsync();

        data.GetProperty("state").GetString().Should().Be(PaymentsOnboardingState.AwaitingVerification);
        data.GetProperty("requirementsDue").GetInt32().Should().Be(14);
        // The account id is still named — it is what the founder needs to look the tenant up.
        data.GetProperty("connectedAccountId").GetString().Should().Be("acct_verification");
    }

    [Fact]
    public async Task The_payload_never_carries_a_requirement_FIELD_NAME()
    {
        // The count is a number the owner can act on ("there is still a form"); the names are their
        // own identity data — a representative's address, a tax id — and Stripe shows them those on
        // the page where they can actually fill them in. Asserted on the whole payload rather than
        // on one property, because the way this regresses is somebody adding a helpful extra field.
        var raw = (await AskAsync()).GetRawText();

        raw.Should().NotContain("currentlyDue");
        raw.Should().NotContain("currently_due");
        raw.Should().NotContain("field.");
        // `commissionBps` was added deliberately, and this list is where that deliberation had to
        // happen — the assertion is an exact set precisely so a new field cannot arrive unnoticed.
        // It is Sofra's own rate, not the restaurant's identity data, and the endpoint is admin-only,
        // so it is disclosure the owner is entitled to rather than a leak.
        //
        // `dashboardUrl` left the same way it arrived: under Connect Express it named a Stripe
        // login the restaurant does not have. Its replacement, `paymentsLinkUrl`, is a page of
        // ours that mints a short-lived Stripe link per click, and it is null until that page
        // exists. This exact-set assertion is what caught the rename — keep it exact.
        (await AskAsync()).EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo(
                ["state", "connectedAccountId", "paymentsLinkUrl", "requirementsDue", "commissionBps"]);
    }
}

/// <summary>Stripe says the account can charge. The control — without it, hardcoding the middle
/// state would satisfy the class above.</summary>
[Collection("Database Lane 4")]
public class PaymentsOnboardingVerifiedTests : PaymentsOnboardingVerificationTestsBase
{
    public PaymentsOnboardingVerifiedTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    // A verified account still lists future requirements ahead of a deadline; the count must NOT
    // ride along, because a number beside "you are set up" reads as a problem where there is none.
    protected override StripeConnectedAccount? Account => new("acct_verification", true, 2);

    [Fact]
    public async Task Charges_enabled_is_configured_and_reports_no_outstanding_count()
    {
        var data = await AskAsync();

        data.GetProperty("state").GetString().Should().Be(PaymentsOnboardingState.Configured);
        data.GetProperty("requirementsDue").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

/// <summary>
/// The account could not be read — the box key does not carry <c>Accounts → read</c>, which is the
/// state of every box until §9 P0(b) is decided.
/// </summary>
[Collection("Database Lane 4")]
public class PaymentsOnboardingUnreadableAccountTests : PaymentsOnboardingVerificationTestsBase
{
    public PaymentsOnboardingUnreadableAccountTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override StripeConnectedAccount? Account => null;

    [Fact]
    public async Task Falls_back_to_P7a_answer_rather_than_guessing_or_blanking()
    {
        // 200 in P7a's shape, exactly. NOT `awaitingVerification`: telling a restaurant that has
        // been taking cards for a month that Stripe is still checking them would be a fabrication
        // built out of our own missing permission. And not a 500 — an optional read that can take
        // the page down is not optional.
        var data = await AskAsync();

        data.GetProperty("state").GetString().Should().Be(PaymentsOnboardingState.Configured);
        data.GetProperty("connectedAccountId").GetString().Should().Be("acct_verification");
        data.GetProperty("requirementsDue").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
