using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

public sealed class MobileAccountLinkHttpTests
{
    [Theory]
    [InlineData("verify")]
    [InlineData("recover")]
    public async Task Landing_is_static_navigation_with_no_grant_or_account_action(string action)
    {
        await using var factory = new MobileLinksFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new("https://links.example"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/r/alpha/" + action);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not change your account or sign you in", html, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("disabled")]
    [InlineData("read-only")]
    [InlineData("unknown")]
    [InlineData("host")]
    [InlineData("contract")]
    [InlineData("unsupported-action")]
    public async Task Unapproved_or_credential_bearing_navigation_is_refused(string scenario)
    {
        await using var factory = new MobileLinksFactory(enabled: scenario != "disabled",
            status: scenario == "read-only" ? "read-only" : "active", contract: scenario == "contract" ? 2 : 1);
        using var client = factory.CreateClient(new() { BaseAddress = new(scenario == "host" ? "https://other.example" : "https://links.example") });
        var path = scenario switch
        {
            "query" => "/r/alpha/verify?proof=private-navigation-must-not-be-reflected",
            "unknown" => "/r/unknown/verify",
            "unsupported-action" => "/r/alpha/finish",
            _ => "/r/alpha/verify"
        };
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
