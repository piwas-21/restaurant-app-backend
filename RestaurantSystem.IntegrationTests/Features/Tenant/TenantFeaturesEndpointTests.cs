using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

/// <summary>
/// Proves the tenant rollout contract through the real ASP.NET pipeline rather than only through
/// the controller. Route reachability, anonymous access, DI binding, JSON naming, and cache
/// policy are all properties that a controller-only test cannot catch.
/// </summary>
[Collection("Database Lane 3")]
public sealed class TenantFeaturesEndpointTests : IAsyncLifetime
{
    private const string Route = "/api/tenant/features";

    private readonly DatabaseFixture _databaseFixture;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public TenantFeaturesEndpointTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(
            _databaseFixture.ConnectionString,
            new Dictionary<string, string> { ["TenantFeatures:ServerWorkspaceV2"] = "true" });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Anonymous_request_gets_the_tenant_flag_with_the_public_wire_contract()
    {
        var response = await _client.GetAsync(new Uri(Route, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = root.GetProperty("data");
        data.GetProperty("serverWorkspaceV2").GetBoolean().Should().BeTrue();
        data.TryGetProperty("ServerWorkspaceV2", out _).Should().BeFalse();
    }
}
