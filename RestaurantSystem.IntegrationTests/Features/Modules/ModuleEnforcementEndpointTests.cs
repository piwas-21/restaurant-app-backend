using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Modules;

/// <summary>
/// Module enforcement through the REAL MVC pipeline (sofra ADR-010 / S11, O5).
///
/// The unit tests pin the decision logic and the reflection tests pin that the attributes
/// are attached; neither exercises the pipeline, where the interactions that actually decide
/// the response live — filter ordering against <c>ApiKeyAuthFilter</c>, the SSE endpoints'
/// <c>[Produces("text/event-stream")]</c>, and the precedence of AuthorizationMiddleware over
/// MVC filters. This class builds a host with enforcement ON and asserts what a caller sees.
/// </summary>
[Collection("Database Lane 2")]
public class ModuleEnforcementEndpointTests : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public ModuleEnforcementEndpointTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    public async Task InitializeAsync()
    {
        // A till-only tenant: core + cashier bought; kitchen-board / server / reservations /
        // loyalty / printing NOT. `cashier` without `server` on purpose — it is the pairing
        // that exposed the shared-stream bug below.
        _factory = new TestWebApplicationFactory(_databaseFixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = "core,cashier",
            });
        _client = _factory.CreateClient();

        await _databaseFixture.ResetDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        await TestDataSeeder.SeedBasicDataAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private void AsAdmin()
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Admin");
        _client.DefaultRequestHeaders.Remove(TestAuthHandler.RoleHeader);
        _client.DefaultRequestHeaders.Remove(TestAuthHandler.AnonymousHeader);
        _client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
    }

    private void AsAnonymous()
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Admin");
        _client.DefaultRequestHeaders.Remove(TestAuthHandler.RoleHeader);
        _client.DefaultRequestHeaders.Remove(TestAuthHandler.AnonymousHeader);
        _client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
    }

    private static async Task<ApiResponse<object>?> ReadBody(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<ApiResponse<object>>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    // ── A module the tenant did not buy ──────────────────────────────────────
    [Theory]
    [InlineData("/api/Reservations")]              // reservations — controller-level gate
    [InlineData("/api/FidelityPoints/balance")]    // loyalty
    [InlineData("/api/UserGroup")]                 // loyalty
    public async Task An_unbought_module_answers_404_with_ModuleNotEnabled(string url)
    {
        AsAdmin();

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadBody(response))?.ErrorCode.Should().Be(ErrorCodes.ModuleNotEnabled);
    }

    [Fact]
    public async Task An_SSE_route_still_returns_the_JSON_denial_body()
    {
        // [Produces("text/event-stream")] is a plain IResultFilter, so an authorization
        // short-circuit skips it and the ApiResponse serialises normally. Pinned because
        // nothing else keeps that true.
        AsAdmin();

        var response = await _client.GetAsync("/api/Events/kitchen");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task The_shared_service_stream_survives_on_cashier_alone()
    {
        // This tenant bought `cashier` and NOT `server`, and /api/Events/service is the feed
        // BOTH the till and the floor view read. Gated on `server` alone it 404'd here, and
        // the till would have rendered perfectly while never receiving an order — silent.
        // A 200 would hang (it is an SSE stream), so assert only that it is not the denial.
        AsAdmin();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Events/service");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        }
        catch (OperationCanceledException)
        {
            // The stream opened and stayed open — which is itself the pass condition.
        }
    }

    // ── A module the tenant DID buy ──────────────────────────────────────────
    [Fact]
    public async Task A_bought_module_is_reachable()
    {
        AsAdmin();

        var response = await _client.GetAsync("/api/Orders/z-report");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Core_surfaces_are_untouched_by_enforcement()
    {
        AsAnonymous();

        var response = await _client.GetAsync("/api/restaurant-info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── The gate runs BEHIND authentication, not in front of it ──────────────
    [Fact]
    public async Task An_anonymous_caller_is_challenged_before_the_module_gate_is_reached()
    {
        // AuthorizationMiddleware consumes [Authorize] metadata and short-circuits BEFORE the
        // MVC filter pipeline, so on an authorized endpoint a guest never reaches the module
        // gate. The guarantee this attribute makes is therefore "404 once the caller clears
        // authentication and role checks" — NOT "404 for everyone". Asserted rather than
        // assumed, because the attribute's own documentation depends on which it is.
        AsAnonymous();

        var response = await _client.GetAsync("/api/FidelityPoints/balance");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_endpoint_with_no_authorize_metadata_is_404_for_a_guest_too()
    {
        // ReservationsController carries no controller-level AuthorizeAttribute, so here the
        // module gate IS the first thing a guest meets — and the public booking form correctly
        // sees "no such feature" rather than a login prompt.
        AsAnonymous();

        var response = await _client.GetAsync("/api/Reservations/available-slots");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadBody(response))?.ErrorCode.Should().Be(ErrorCodes.ModuleNotEnabled);
    }

    // ── The gate wins over the printer-app's API-key filter ──────────────────
    [Fact]
    public async Task The_module_gate_beats_the_api_key_filter_on_the_printer_feed()
    {
        // Both are unordered IAuthorizationFilters; controller scope runs before action scope,
        // so the module gate decides first and a printing-less tenant gets 404 rather than a
        // 401 that would read as "wrong key". Pinned because an IOrderedFilter on either would
        // silently flip it.
        AsAnonymous();

        var response = await _client.GetAsync("/api/orders/printer-feed");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Discovery endpoint ───────────────────────────────────────────────────
    [Fact]
    public async Task The_discovery_endpoint_publishes_the_effective_set_anonymously()
    {
        AsAnonymous();

        var response = await _client.GetAsync("/api/tenant/modules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        data.GetProperty("enforced").GetBoolean().Should().BeTrue();
        data.GetProperty("modules").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { ModuleIds.Core, ModuleIds.Cashier });
    }
}

/// <summary>
/// The same pipeline with NO module list — the live RUMI shape. Everything must work.
/// </summary>
[Collection("Database Lane 2")]
public class ModuleEnforcementUnrestrictedTests : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public ModuleEnforcementUnrestrictedTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    public async Task InitializeAsync()
    {
        // Enforce=true with an EMPTY list, which is the worst-case misconfiguration of the
        // legacy install: it must still be unrestricted.
        _factory = new TestWebApplicationFactory(_databaseFixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = "",
            });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Test-Admin", "true");

        await _databaseFixture.ResetDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        await TestDataSeeder.SeedBasicDataAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("/api/Reservations")]
    [InlineData("/api/UserGroup")]
    [InlineData("/api/Orders/z-report")]
    public async Task Every_gated_surface_stays_reachable_without_a_module_list(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_discovery_endpoint_reports_unrestricted()
    {
        var response = await _client.GetAsync("/api/tenant/modules");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        data.GetProperty("enforced").GetBoolean().Should().BeFalse();
        data.GetProperty("modules").EnumerateArray().Should().HaveCount(ModuleIds.All.Count);
    }
}
