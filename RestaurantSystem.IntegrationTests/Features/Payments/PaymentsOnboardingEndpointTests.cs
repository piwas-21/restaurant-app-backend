using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Queries.GetPaymentsOnboardingQuery;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// P7a (SOFRA-PAYMENTS-PLAN §9) — <c>GET /api/payments/onboarding</c> through the real MVC
/// pipeline.
///
/// <para>
/// This is the admin-only sibling of <c>/availability</c>, and the property under test is that
/// the two really are different endpoints rather than the same one with a longer DTO: this one
/// names the connected account, which the anonymous endpoint must never do, and it is reachable
/// only by an admin of a tenant that bought the module.
/// </para>
/// <para>
/// The two refusals are each asserted for their own reason. A non-admin gets <b>403</b>, from
/// <c>AuthorizationMiddleware</c>, which runs ahead of the MVC filter pipeline — so the module
/// gate never even runs and the answer cannot leak what the tenant bought. A tenant without the
/// module gets <b>404 with <c>errorCode: ModuleNotEnabled</c></b>, and the code is the assertion:
/// a bare 404 is also what a typo'd route and an old backend answer, so a test that checked only
/// the status would pass against a route that does not exist at all (O5 trap 2).
/// </para>
/// </summary>
public abstract class PaymentsOnboardingEndpointTestsBase : SettingsDrivenEndpointTest
{
    protected PaymentsOnboardingEndpointTestsBase(DatabaseFixture fixture) : base(fixture) { }

    // Configuration is the whole input; the endpoint reads no database at all.
    protected override bool ResetDatabase => false;

    protected const string Url = "/api/payments/onboarding";

    /// <summary>A configured tenant, as an inline settings block. Fake key, no network is made.</summary>
    protected static Dictionary<string, string> StripeConfigured() => new()
    {
        ["Stripe:Enabled"] = "true",
        ["Stripe:PlatformApiKey"] = "rk_test_onboarding",  // pragma: allowlist secret
        ["Stripe:ConnectedAccountId"] = "acct_onboarding",
    };

    /// <summary>
    /// Stop being a guest. The base class hands out an ANONYMOUS client, because every other
    /// payments endpoint is anonymous — this is the one that is not.
    /// </summary>
    /// <remarks>
    /// The header is removed from the client rather than from a per-request message, because it is
    /// a DEFAULT header: a `HttpRequestMessage.Headers.Remove` on a header the client adds later
    /// removes nothing, and the request still arrives unauthenticated. Safe to mutate here because
    /// xUnit constructs a new instance of the test class — and so runs `InitializeAsync` — for
    /// every single test, so no other test ever sees this client.
    /// </remarks>
    private void StopBeingAnonymous() =>
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.AnonymousHeader);

    /// <summary>The admin the tab is for.</summary>
    protected async Task<HttpResponseMessage> AskAsAdmin()
    {
        StopBeingAnonymous();
        Client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        return await Client.GetAsync(Url);
    }

    protected async Task<HttpResponseMessage> AskAsRole(string role)
    {
        // `TestAuthHandler` ignores the role header while the anonymous one is present, so a
        // role test that forgot this would assert 403 and be handed 401 — right verdict, wrong
        // reason, and it would keep passing if the endpoint stopped being admin-only.
        StopBeingAnonymous();
        Client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        return await Client.GetAsync(Url);
    }
}

/// <summary>A tenant that bought the module and whose Stripe credentials reached the box.</summary>
[Collection("Database Lane 3")]
public class PaymentsOnboardingConfiguredTests : PaymentsOnboardingEndpointTestsBase
{
    public PaymentsOnboardingConfiguredTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings
    {
        get
        {
            var settings = StripeConfigured();
            settings["Modules:Enforce"] = "true";
            settings["Modules:Enabled"] = "core,online-payments";
            return settings;
        }
    }

    [Fact]
    public async Task An_admin_is_told_which_account_the_money_settles_to()
    {
        var response = await AskAsAdmin();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadData(response);
        data.GetProperty("state").GetString().Should().Be(PaymentsOnboardingState.Configured);
        data.GetProperty("connectedAccountId").GetString().Should().Be("acct_onboarding");
        data.GetProperty("dashboardUrl").GetString()
            .Should().Be(GetPaymentsOnboardingQueryHandler.StripeDashboardUrl);
    }

    [Theory]
    [InlineData("Cashier")]
    [InlineData("Server")]
    [InlineData("KitchenStaff")]
    public async Task Back_of_house_is_refused_with_403_not_404(string role)
    {
        // 403 and not 404, on purpose: the caller IS on a tenant that has the module, so the
        // honest answer is "not you", and it is produced before the module gate can run.
        (await AskAsRole(role)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_anonymous_caller_gets_401_and_learns_nothing()
    {
        // The base client is anonymous. `/availability` answers this same caller 200 — the
        // difference between the two endpoints is the point of the slice.
        (await Client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_anonymous_availability_endpoint_still_says_nothing_but_a_boolean()
    {
        // The regression this guards: "the admin needs the account id" is one small edit away
        // from putting it on the public DTO, which every checkout page load fetches.
        var response = await Client.GetAsync("/api/payments/availability");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadData(response);
        data.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["available"]);
    }
}

/// <summary>
/// The live fleet's shape: the module gate is unrestricted (RUMI's empty list) and Stripe is not
/// configured at all.
/// </summary>
[Collection("Database Lane 3")]
public class PaymentsOnboardingUnconfiguredTests : PaymentsOnboardingEndpointTestsBase
{
    public PaymentsOnboardingUnconfiguredTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "",
    };

    [Fact]
    public async Task Says_notConfigured_and_names_no_account()
    {
        var response = await AskAsAdmin();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadData(response);
        data.GetProperty("state").GetString().Should().Be(PaymentsOnboardingState.NotConfigured);
        data.GetProperty("connectedAccountId").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

/// <summary>
/// Provisioning switched Stripe on and wrote the account, but no platform key ever reached the
/// box — the state a half-finished provision leaves behind (see the availability suite's
/// mid-provisioning class for why this is real rather than hypothetical).
/// </summary>
[Collection("Database Lane 3")]
public class PaymentsOnboardingMidProvisioningTests : PaymentsOnboardingEndpointTestsBase
{
    public PaymentsOnboardingMidProvisioningTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "core,online-payments",
        ["Stripe:Enabled"] = "true",
        ["Stripe:PlatformApiKey"] = "",
        ["Stripe:ConnectedAccountId"] = "acct_half_provisioned",
    };

    [Fact]
    public async Task Reports_notConfigured_and_withholds_the_account_it_does_have()
    {
        // The account id is RIGHT THERE in configuration, and reporting it would show an owner
        // an account their restaurant cannot transact on — one leg answering for both, which is
        // the exact mistake S8 had to fix on availability. So the id is bound to the gateway's
        // verdict, not to the raw setting.
        var response = await AskAsAdmin();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadData(response);
        data.GetProperty("state").GetString().Should().Be(PaymentsOnboardingState.NotConfigured);
        data.GetProperty("connectedAccountId").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

/// <summary>A tenant that never bought the module: the class gate answers, with its code.</summary>
[Collection("Database Lane 3")]
public class PaymentsOnboardingModuleOffTests : PaymentsOnboardingEndpointTestsBase
{
    public PaymentsOnboardingModuleOffTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "core,cashier",
    };

    [Fact]
    public async Task Answers_404_carrying_ModuleNotEnabled()
    {
        var response = await AskAsAdmin();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // The discriminator, not the status. A bare 404 is also what a wrong route answers, so
        // asserting only the status would pass against an endpoint that was never added.
        (await ReadErrorCode(response)).Should().Be(ErrorCodes.ModuleNotEnabled);
    }
}
