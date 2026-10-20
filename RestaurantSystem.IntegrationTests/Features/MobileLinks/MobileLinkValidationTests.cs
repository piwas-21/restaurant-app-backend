using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sofra.MobileService.Links;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

public sealed class MobileLinkValidationTests
{
    [Theory]
    [InlineData(false, "", true)]
    [InlineData(true, "", false)]
    [InlineData(true, "https://links.example", true)]
    [InlineData(false, "http://links.example", false)]
    [InlineData(true, "https://links.example:8443", false)]
    [InlineData(true, "https://links.example:443", false)]
    [InlineData(true, "https://user@links.example", false)]
    [InlineData(true, "https://links.example/path", false)]
    [InlineData(true, "https://links.example?operation=borrowed", false)]
    public void OnlyExplicitCanonicalHttpsOriginCanEnableReturnLanding(bool enabled, string origin, bool valid)
    {
        var result = new MobileLinkSettingsValidator().Validate(null, new() { Enabled = enabled, Origin = origin });
        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData("Origin", "http://links.example", true)]
    [InlineData("UnknownPrivateSetting", "must-be-refused", false)]
    public async Task InvalidAndUnknownSettingsPreventStartup(string key, string value, bool validation)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MobileLinks:Enabled"] = "true",
            ["MobileLinks:Origin"] = "https://links.example",
            [$"MobileLinks:{key}"] = value
        });
        builder.Services.AddMobileLinks(builder.Configuration);
        using var host = builder.Build();
        if (validation) await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        else await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }
}
