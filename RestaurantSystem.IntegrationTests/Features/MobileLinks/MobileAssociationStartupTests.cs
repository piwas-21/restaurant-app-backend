using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sofra.MobileService.Links;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

public sealed class MobileAssociationStartupTests
{
    [Theory]
    [InlineData("valid", false)]
    [InlineData("AppleApplicationId", true)]
    [InlineData("AndroidSha256Fingerprints:0", true)]
    [InlineData("UnknownPrivateSetting", true)]
    public async Task StartupRequiresReviewedCompleteMetadataAndRefusesUnknownSettings(string key, bool invalid)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        var configuration = new Dictionary<string, string?>
        {
            ["MobileLinks:Enabled"] = "true",
            ["MobileLinks:Origin"] = "https://links.example",
            ["MobileAssociations:Enabled"] = "true",
            ["MobileAssociations:AppleApplicationId"] = MobileAssociationFixture.AppleId,
            ["MobileAssociations:AndroidPackage"] = MobileAssociationFixture.AndroidPackage,
            ["MobileAssociations:AndroidSha256Fingerprints:0"] = MobileAssociationFixture.Fingerprint
        };
        if (invalid) configuration[$"MobileAssociations:{key}"] = key == "AppleApplicationId" ? string.Empty : "must-be-refused";
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddMobileLinks(builder.Configuration);
        builder.Services.AddMobileAssociations(builder.Configuration);
        using var host = builder.Build();
        if (!invalid)
        {
            await host.StartAsync();
            await host.StopAsync();
        }
        else if (key == "UnknownPrivateSetting") await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        else await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }
}
