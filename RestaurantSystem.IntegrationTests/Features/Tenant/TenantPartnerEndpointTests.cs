using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

/// <summary>
/// <c>GET /api/tenant/partner</c> through the REAL pipeline
/// (workspace docs/plans/SOFRA-PARTNER-PLAN.md §11, slice S4a).
///
/// Asserted against a host rather than against the controller, because three of the four
/// properties this endpoint has to keep are properties of the PIPELINE and not of the class:
/// the route the footer calls, reachability with no credentials at all, and the binding of
/// Partner:Name / Partner:Url out of configuration. A test that constructed the controller
/// itself would stay green through a route rename or an [Authorize] and the footer would
/// silently lose its credit on every tenant.
/// </summary>
[Collection("Database Lane 3")]
public class TenantPartnerEndpointTests : IAsyncLifetime
{
    private const string Route = "/api/tenant/partner";

    private readonly DatabaseFixture _databaseFixture;
    private readonly List<IDisposable> _disposables = new();

    public TenantPartnerEndpointTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// A host configured with <paramref name="settings"/>, and a client carrying NO credentials —
    /// not the default Customer the test auth handler otherwise invents, because the whole point
    /// of this endpoint is that the footer renders before anyone logs in.
    /// </summary>
    private HttpClient CreateClient(Dictionary<string, string> settings)
    {
        var factory = new TestWebApplicationFactory(_databaseFixture.ConnectionString, settings);
        _disposables.Add(factory);

        var client = factory.CreateClient();
        _disposables.Add(client);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        return client;
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the footer must be able to ask on every tenant, partnered or not");

        var payload = await response.Content.ReadAsStringAsync();
        // Cloned: the JsonDocument is disposed with the using, and the caller reads afterwards.
        using var body = JsonDocument.Parse(payload);
        return body.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task A_guest_with_no_credentials_gets_the_configured_partner()
    {
        var client = CreateClient(new Dictionary<string, string>
        {
            ["Partner:Name"] = "Solution Eva",
            ["Partner:Url"] = "https://solutioneva.com",
        });

        var data = await ReadDataAsync(await client.GetAsync(new Uri(Route, UriKind.Relative)));

        // The WIRE SHAPE is the contract — these two camelCase names are what the footer reads.
        data.GetProperty("name").GetString().Should().Be("Solution Eva");
        data.GetProperty("url").GetString().Should().Be("https://solutioneva.com/");
    }

    [Fact]
    public async Task An_unset_partner_answers_200_with_nulls_rather_than_404()
    {
        // No Partner keys at all: every tenant provisioned before the deploy slice (S3b), which is
        // every tenant today. 200-with-nulls is the decision recorded in the controller docstring —
        // a 404 here would be indistinguishable from a tenant running an older image.
        var client = CreateClient(new Dictionary<string, string>());

        var data = await ReadDataAsync(await client.GetAsync(new Uri(Route, UriKind.Relative)));

        data.GetProperty("name").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_empty_name_publishes_nothing_even_when_a_url_is_set()
    {
        // What provision-tenant.sh writes when partner_attribution is false: BOTH values emptied.
        // The url must not survive on its own — a bare link with no label is not attribution, and
        // it would still name the partner through the domain.
        var client = CreateClient(new Dictionary<string, string>
        {
            ["Partner:Name"] = "   ",
            ["Partner:Url"] = "https://solutioneva.com",
        });

        var data = await ReadDataAsync(await client.GetAsync(new Uri(Route, UriKind.Relative)));

        data.GetProperty("name").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null,
            "a url with no name is not something the footer can render");
    }

    [Theory]
    // A hand-edited .env on the box never passes through provision-tenant.sh, so these reach the
    // process. Each one becomes an href on a public page if it is served.
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("http://solutioneva.com")]
    [InlineData("//solutioneva.com")]
    [InlineData("solutioneva.com")]
    public async Task A_url_that_is_not_absolute_https_is_withheld_while_the_name_is_still_served(
        string hostileUrl)
    {
        var client = CreateClient(new Dictionary<string, string>
        {
            ["Partner:Name"] = "Solution Eva",
            ["Partner:Url"] = hostileUrl,
        });

        var data = await ReadDataAsync(await client.GetAsync(new Uri(Route, UriKind.Relative)));

        data.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null,
            "only an absolute https:// URL may become an href on a public page");
        data.GetProperty("name").GetString().Should().Be("Solution Eva",
            "withholding a bad link must not also withhold a correct credit");
    }

    [Fact]
    public async Task The_route_is_the_one_the_footer_calls()
    {
        // Named separately so a rename reads as "the route moved" rather than as a missing partner.
        var client = CreateClient(new Dictionary<string, string> { ["Partner:Name"] = "Solution Eva" });

        (await client.GetAsync(new Uri(Route, UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
