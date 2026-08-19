using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

/// <summary>
/// <c>GET /api/tenant/today</c> through the REAL pipeline.
///
/// <see cref="TenantTodayTests"/> constructs the controller directly, so it proves the day is
/// right and nothing else: renaming the route to <c>today-zz</c> or swapping
/// <c>[AllowAnonymous]</c> for <c>[Authorize]</c> leaves all four of those green while the
/// reservation form that calls this (frontend #517) gets a 404 or a 401. Both of those are
/// properties of the pipeline, so they are asserted here, against a host.
/// </summary>
[Collection("Database Lane 3")]
public class TenantTodayEndpointTests : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public TenantTodayEndpointTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    public Task InitializeAsync()
    {
        // A zone that is nobody's default, so a hardcoded or fallback answer cannot pass as the
        // configured one — and one east of UTC, which is where the day differs first.
        _factory = new TestWebApplicationFactory(_databaseFixture.ConnectionString,
            new Dictionary<string, string> { ["Localization:TimeZone"] = "Asia/Tokyo" });
        _client = _factory.CreateClient();
        // Not the default Customer this handler otherwise invents: the point of the endpoint is
        // that a guest with no credentials at all can reach it before the booking form renders.
        _client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_guest_with_no_credentials_gets_the_tenants_day()
    {
        var response = await _client.GetAsync(new Uri("/api/tenant/today", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the booking form calls this before anyone logs in");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");

        // The WIRE SHAPE is the contract, and it is what the client parses (`isCalendarDay`): a
        // slip from DateOnly to DateTime keeps every unit test green and hands the browser an
        // instant it must re-read in some zone — which is the defect this endpoint exists to end.
        data.GetProperty("date").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
        data.GetProperty("timeZone").GetString().Should().Be("Asia/Tokyo");

        // Tokyo is UTC+9 and never observes DST, so its day is UTC's day or the one after it —
        // never before. Anchored to the real instant rather than a literal date.
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var served = DateOnly.Parse(data.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        served.Should().BeOneOf(utcToday, utcToday.AddDays(1));

        response.Headers.CacheControl!.NoStore.Should().BeTrue("a cached day is a wrong day within hours");
    }

    [Fact]
    public async Task The_route_is_the_one_the_clients_call()
    {
        // Named separately from the assertion above so a rename reads as "the route moved" rather
        // than as a broken day.
        (await _client.GetAsync(new Uri("/api/tenant/today", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
