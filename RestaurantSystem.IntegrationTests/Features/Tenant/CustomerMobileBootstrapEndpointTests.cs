using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

[Collection("Database Lane 3")]
public sealed class CustomerMobileBootstrapEndpointTests(DatabaseFixture databaseFixture) : IAsyncLifetime
{
    private const string Route = "/api/tenant/mobile/bootstrap";
    private const string TenantId = "11111111-1111-4111-8111-111111111111";

    public Task InitializeAsync() => databaseFixture.ResetDatabaseAsync();
    public Task DisposeAsync() => databaseFixture.ResetDatabaseAsync();

    [Fact]
    public async Task Missing_native_configuration_is_unavailable_without_affecting_existing_tenant_routes()
    {
        await using var factory = new TestWebApplicationFactory(databaseFixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        (await client.GetAsync(new Uri(Route, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri("/api/tenant/modules", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(1, "supported", "enabled")]
    [InlineData(999, "unavailable", "unavailable")]
    public async Task Public_bootstrap_binds_identity_currency_capabilities_and_compatibility(
        int contractVersion, string compatibility, string menu)
    {
        await using (var context = databaseFixture.CreateContext())
        {
            var restaurant = await context.RestaurantInfo.SingleAsync();
            restaurant.Currency = "EUR";
            await context.SaveChangesAsync();
        }
        await using var factory = new TestWebApplicationFactory(databaseFixture.ConnectionString, new Dictionary<string, string>
        {
            ["CustomerMobile:Enabled"] = "true",
            ["CustomerMobile:TenantId"] = TenantId,
            ["CustomerMobile:Lifecycle"] = "active",
            ["CustomerMobile:EnabledCapabilities:0"] = "menu",
            ["Localization:DefaultLanguage"] = "fr",
            ["Localization:TimeZone"] = "Europe/Zurich",
            ["Modules:Enforce"] = "true",
            ["Modules:Enabled"] = "core"
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        var response = await client.GetAsync(new Uri($"{Route}?contractVersion={contractVersion}", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("tenantId").GetString().Should().Be(TenantId);
        data.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        data.GetProperty("currency").GetString().Should().Be("EUR");
        data.GetProperty("locale").GetString().Should().Be("fr");
        data.GetProperty("timezone").GetString().Should().Be("Europe/Zurich");
        data.GetProperty("compatibility").GetString().Should().Be(compatibility);
        data.GetProperty("capabilities").GetProperty("menu").GetString().Should().Be(menu);
        data.GetProperty("capabilities").GetProperty("ordering").GetString().Should().NotBe("enabled");
        data.TryGetProperty("email", out _).Should().BeFalse();
    }
}
