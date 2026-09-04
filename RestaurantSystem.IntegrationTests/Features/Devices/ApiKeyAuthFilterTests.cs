using System.Net;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Devices;

/// <summary>
/// Backend #475 — <c>ApiKeyAuthFilter</c> FAILED OPEN on a blank key: it returned early and the
/// endpoint served everyone. It is the only caller check on the printer fleet's endpoints, and
/// <c>GET /api/orders/printer-feed</c> returns confirmed orders — customer names, phone numbers,
/// addresses.
///
/// <para>
/// The shape mattered more than the odds: <c>PrinterSettings:ApiKey</c> defaults to <c>""</c> in
/// both appsettings.json and appsettings.Development.json, so a tenant provisioned without one
/// came up serving its order feed to anyone — and every functional check passed, because from the
/// printer-app's point of view an open feed works perfectly.
/// </para>
///
/// <para>
/// The blank-key case is exercised with a host of its own, because it is a CONFIGURATION state:
/// the shared test host now carries a real key (appsettings.Test.json) precisely so the rest of
/// the suite goes through the same closed door production does.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class ApiKeyAuthFilterTests : IntegrationTestBase
{
    private const string Feed = "/api/orders/printer-feed";

    public ApiKeyAuthFilterTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task No_key_at_all_is_refused()
    {
        var response = await Client.GetAsync(Feed);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_key_is_refused()
    {
        Client.DefaultRequestHeaders.Add(DeviceApiKeyHeader, "not-the-configured-key");

        var response = await Client.GetAsync(Feed);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The control. A filter that refused everything would satisfy both assertions above while
    /// taking every restaurant's printer offline.
    /// </summary>
    [Fact]
    public async Task The_configured_key_is_accepted()
    {
        AuthenticateAsDevice();

        var response = await Client.GetAsync(Feed);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// THE defect. A host with no key configured, in an environment that is not Development —
    /// exactly a tenant provisioned without <c>PrinterSettings:ApiKey</c>. Before #475 this
    /// answered 200 and served the order feed.
    /// </summary>
    [Fact]
    public async Task An_UNCONFIGURED_key_refuses_rather_than_opening_the_feed()
    {
        using var factory = new TestWebApplicationFactory(
            DatabaseFixture.ConnectionString,
            settings: new Dictionary<string, string> { ["PrinterSettings:ApiKey"] = string.Empty },
            disableApplicationHostedServices: true);
        using var client = factory.CreateClient();

        // The key IS sent, and that is what makes this test able to fail. Both the unconfigured
        // branch and the missing-header branch return a byte-identical 401, so a request with no
        // header would go green even if the `settings:` override never landed — the one thing this
        // test exists to prove is the one thing it could not then see. Sending a VALID key means a
        // landed override gives 401 and a lost one gives 200.
        client.DefaultRequestHeaders.Add(DeviceApiKeyHeader, TestPrinterApiKey);

        var response = await client.GetAsync(Feed);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unconfigured key must disable the printer feature, not its authentication — "
            + "and a 200 here means the blank-key override did not land");
    }

    /// <summary>
    /// And it refuses the WRITES too, not only the read. The three device endpoints have no user
    /// to authorize either, so a fix that only covered the feed would leave a tenant's fleet
    /// status and print acknowledgements writable by anyone.
    /// </summary>
    [Theory]
    [InlineData("/api/devices/heartbeat")]
    [InlineData("/api/devices/print-acks")]
    [InlineData("/api/devices/events")]
    public async Task An_UNCONFIGURED_key_refuses_the_device_writes_too(string path)
    {
        using var factory = new TestWebApplicationFactory(
            DatabaseFixture.ConnectionString,
            settings: new Dictionary<string, string> { ["PrinterSettings:ApiKey"] = string.Empty },
            disableApplicationHostedServices: true);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { platform = "Android" }),
        };
        // A VALID key, for the reason given above: without it a lost override is indistinguishable
        // from the fix working.
        request.Headers.Add(DeviceApiKeyHeader, TestPrinterApiKey);
        request.Headers.Add("X-Device-Id", "dev-probe");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
