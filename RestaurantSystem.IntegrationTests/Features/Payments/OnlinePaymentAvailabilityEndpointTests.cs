using System.Net;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S8 (SOFRA-PAYMENTS-PLAN §5) — <c>GET /api/payments/availability</c> through the real MVC
/// pipeline.
///
/// <para>
/// The property under test is <b>not</b> "does it return a boolean". It is that the two legs of
/// the plan's "module flag AND configured account" are both actually load-bearing, because each
/// one alone fails OPEN on a real install: the module gate reads RUMI's absent module list as
/// unrestricted, and a tenant that bought the module but has not been given Stripe credentials
/// yet is a normal state of provisioning. Either leg answering "yes" on its own is a checkout
/// page offering a payment method the restaurant cannot take.
/// </para>
/// </summary>
public abstract class OnlinePaymentAvailabilityEndpointTestsBase : SettingsDrivenEndpointTest
{
    protected OnlinePaymentAvailabilityEndpointTestsBase(DatabaseFixture fixture) : base(fixture) { }

    // No database state is read or written by this endpoint — it answers from configuration alone —
    // so the reset the base does by default is pure cost here.
    protected override bool ResetDatabase => false;

    /// <summary>A configured tenant, as an inline settings block. Fake key, no network is made.</summary>
    protected static Dictionary<string, string> StripeConfigured() => new()
    {
        ["Stripe:Enabled"] = "true",
        ["Stripe:PlatformApiKey"] = "rk_test_availability",  // pragma: allowlist secret
        ["Stripe:ConnectedAccountId"] = "acct_availability",
    };

    protected async Task<HttpResponseMessage> Ask() =>
        await Client.GetAsync("/api/payments/availability");

    protected static async Task<bool> ReadAvailable(HttpResponseMessage response) =>
        (await ReadData(response)).GetProperty("available").GetBoolean();
}

/// <summary>
/// The live fleet's shape: no module list at all (so the gate is unrestricted and every module
/// reads as bought) and no Stripe configuration. This is RUMI.
/// </summary>
[Collection("Database Lane 3")]
public class OnlinePaymentAvailabilityUnconfiguredTests : OnlinePaymentAvailabilityEndpointTestsBase
{
    public OnlinePaymentAvailabilityUnconfiguredTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        // Enforce with an EMPTY list — the worst-case misconfiguration of the legacy install,
        // which TenantModules deliberately reads as unrestricted rather than as "nothing on".
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "",
    };

    [Fact]
    public async Task The_module_gate_lets_this_tenant_through_and_the_answer_is_still_no()
    {
        // The whole point of the slice. The 404 never fires here, so if availability were the
        // module flag alone, RUMI's checkout page would offer online payment and the diner would
        // be redirected to a Stripe session that cannot be minted.
        var response = await Ask();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAvailable(response)).Should().BeFalse();
    }
}

/// <summary>
/// A tenant that bought the module AND has its Stripe credentials. The control: without it,
/// hardcoding <c>available: false</c> would satisfy every other assertion in this file.
/// </summary>
[Collection("Database Lane 3")]
public class OnlinePaymentAvailabilityConfiguredTests : OnlinePaymentAvailabilityEndpointTestsBase
{
    public OnlinePaymentAvailabilityConfiguredTests(DatabaseFixture databaseFixture)
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
    public async Task Both_legs_satisfied_answers_yes_to_a_guest()
    {
        var response = await Ask();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAvailable(response)).Should().BeTrue();
    }
}

/// <summary>
/// The tenant bought the module and provisioning switched Stripe ON, but no key ever reached the
/// box. A real, reachable state rather than a hypothetical one — and the state that tells the two
/// candidate implementations apart.
/// </summary>
/// <remarks>
/// <c>provision-tenant.sh</c> derives <c>STRIPE_ENABLED</c> from the registry's module list ("a
/// tenant that did not buy online-payments must not be one env edit away from taking card
/// payments"), and writes it BEFORE the empty-key refusal further down the script;
/// <c>tenant.env.tpl</c> ships <c>STRIPE_PLATFORM_API_KEY</c> empty so a box with no key leaves
/// every tenant on it inert. So <c>Enabled=true</c> with empty credentials is what a half-finished
/// provision leaves behind.
///
/// <para>
/// Without this case the whole suite is satisfied by an availability answer that reads
/// <c>Stripe:Enabled</c> alone — which, because that flag is derived from the module list, IS the
/// module flag, i.e. exactly the single-leg answer this slice exists to prevent. Verified by
/// mutation: collapsing <c>IsConfigured</c> to <c>_settings.Enabled</c> left the other three tests
/// green.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class OnlinePaymentAvailabilityMidProvisioningTests : OnlinePaymentAvailabilityEndpointTestsBase
{
    public OnlinePaymentAvailabilityMidProvisioningTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "core,online-payments",
        ["Stripe:Enabled"] = "true",
        ["Stripe:PlatformApiKey"] = "",
        ["Stripe:ConnectedAccountId"] = "",
    };

    [Fact]
    public async Task The_module_is_bought_and_switched_on_but_uncredentialed_answers_no()
    {
        var response = await Ask();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAvailable(response)).Should().BeFalse();
    }
}

/// <summary>
/// The module leg on its own: Stripe fully configured, but the tenant did not buy the module.
/// </summary>
[Collection("Database Lane 3")]
public class OnlinePaymentAvailabilityUnboughtModuleTests : OnlinePaymentAvailabilityEndpointTestsBase
{
    public OnlinePaymentAvailabilityUnboughtModuleTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings
    {
        get
        {
            var settings = StripeConfigured();
            settings["Modules:Enforce"] = "true";
            settings["Modules:Enabled"] = "core,cashier";
            return settings;
        }
    }

    [Fact]
    public async Task An_unbought_module_answers_404_and_never_reaches_the_handler()
    {
        // Configured Stripe on purpose: the handler would answer `available: true` if it ran, so
        // a 404 here is the class-level gate doing the work and not a coincidence of settings.
        var response = await Ask();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorCode(response)).Should().Be(ErrorCodes.ModuleNotEnabled);
    }
}
