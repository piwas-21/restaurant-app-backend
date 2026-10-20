using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Tenant.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

public sealed class CustomerNativeAuthenticationCapabilitiesTests
{
    [Theory]
    [InlineData("enabled", true, true)]
    [InlineData("capability-withdrawn", false, false)]
    [InlineData("email-disabled", false, true)]
    [InlineData("email-invalid", false, true)]
    [InlineData("google-disabled", true, false)]
    [InlineData("google-invalid", true, false)]
    public void Public_flags_require_current_accounts_capability_and_each_valid_provider_configuration(
        string scenario, bool emailExpected, bool googleExpected)
    {
        var accounts = new CustomerNativeAccountSettings
        {
            Enabled = scenario != "email-disabled",
            LinkOrigin = scenario == "email-invalid" ? "http://links.example" : "https://links.example",
            TenantSlug = "alpha"
        };
        var social = new CustomerNativeSocialSettings
        {
            GoogleEnabled = scenario != "google-disabled",
            GoogleClientIds = scenario == "google-invalid" ? [] : ["native-shared-client"]
        };
        var flags = new CustomerNativeAuthenticationCapabilities(Options.Create(accounts), Options.Create(social))
            .Resolve(scenario != "capability-withdrawn");
        Assert.Equal(emailExpected, flags.AccountLinksEnabled);
        Assert.Equal(googleExpected, flags.GoogleEnabled);
    }
}
