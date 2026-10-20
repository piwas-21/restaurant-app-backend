using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RestaurantSystem.IntegrationTests.Features.MobileDirectory;
using Sofra.MobileService;
using Sofra.MobileService.Links;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

public sealed class MobilePaymentReturnHttpTests
{
    private const string Origin = "https://links.example";
    private static string Path => $"/r/alpha/payment-return/{MobileDirectoryFixture.AlphaId:D}?operation={MobileDirectoryFixture.BetaId:D}";

    [Theory]
    [InlineData("active")]
    [InlineData("read-only")]
    public async Task LandingIsPublicNavigationOnlyWithNoFinancialOrPrivateReferences(string status)
    {
        await using var factory = new LinkFactory(status: status);
        using var client = factory.CreateClient(new() { BaseAddress = new(Origin), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Authorization", "Bearer invalid-navigation-control");
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not confirm payment or cancel your order", html, StringComparison.Ordinal);
        Assert.DoesNotContain(MobileDirectoryFixture.AlphaId.ToString(), html, StringComparison.Ordinal);
        Assert.DoesNotContain(MobileDirectoryFixture.BetaId.ToString(), html, StringComparison.Ordinal);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
        using var other = await client.GetAsync($"/r/alpha/payment-return/{Guid.NewGuid():D}?operation={Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(html, await other.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("wrong-key")]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("upper")]
    [InlineData("unknown-tenant")]
    [InlineData("order-format")]
    [InlineData("wrong-host")]
    public async Task MalformedOrUnapprovedNavigationIsRefused(string scenario)
    {
        await using var factory = new LinkFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new(Origin), AllowAutoRedirect = false });
        var path = scenario switch
        {
            "missing" => Path.Split('?')[0],
            "duplicate" => Path + $"&operation={MobileDirectoryFixture.BetaId:D}",
            "extra" => Path + "&paid=true",
            "wrong-key" => Path.Replace("operation=", "token=", StringComparison.Ordinal),
            "empty" => Path.Split('?')[0] + "?operation=",
            "zero" => Path.Replace(MobileDirectoryFixture.BetaId.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal),
            "upper" => Path.Split('?')[0] + "?operation=AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA",
            "unknown-tenant" => Path.Replace("/alpha/", "/unknown/", StringComparison.Ordinal),
            "order-format" => Path.Replace(MobileDirectoryFixture.AlphaId.ToString(), "not-an-order", StringComparison.Ordinal),
            _ => Path
        };
        if (scenario == "wrong-host") client.DefaultRequestHeaders.Host = "unapproved.example";
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false, "active", 1)]
    [InlineData(true, "disabled", 1)]
    [InlineData(true, "active", 2)]
    public async Task LandingRemainsUnavailableUntilEveryNavigationGateAllowsIt(bool enabled, string status, int contract)
    {
        await using var factory = new LinkFactory(enabled, status, contract);
        using var client = factory.CreateClient(new() { BaseAddress = new(Origin) });
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class LinkFactory(bool enabled = true, string status = "active", int contract = 1)
        : WebApplicationFactory<MobileServiceProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) => config.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
            {
                MobileDirectory = MobileDirectoryFixture.Settings([MobileDirectoryFixture.Alpha with { Status = status, ContractVersions = [contract] }]),
                MobileLinks = new MobileLinkSettings { Enabled = enabled, Origin = Origin }
            }))));
    }
}
