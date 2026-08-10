using System.Text.Json;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// A test host built with a specific CONFIGURATION, driven as an anonymous caller.
///
/// <para>
/// Extracted on its second occurrence (CLAUDE.md general rule 5). The payments endpoints are each
/// tested across several tenant shapes — module bought or not, Stripe configured or not — and each
/// shape needs its own host, because <c>TenantModules</c> and <c>StripeGateway</c> both read their
/// configuration ONCE at startup. That makes "one xUnit class per configuration" the natural unit,
/// and made the factory/client lifecycle the thing being copied.
/// </para>
/// </summary>
public abstract class SettingsDrivenEndpointTest : IAsyncLifetime
{
    private TestWebApplicationFactory _factory = null!;

    protected SettingsDrivenEndpointTest(DatabaseFixture fixture)
    {
        Fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    protected DatabaseFixture Fixture { get; }

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>The configuration THIS class's tenant runs with.</summary>
    protected abstract IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>
    /// Override to false for a suite that needs no database state. Defaults to resetting, because
    /// a payments test that reads a stale order from a previous class is a test that lies.
    /// </summary>
    protected virtual bool ResetDatabase => true;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(Fixture.ConnectionString, Settings);
        Client = _factory.CreateClient();
        // The caller that matters on every payments surface: guest checkout has no account
        // (ADR-004), and the diner coming back from Stripe is not logged in.
        Client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");

        if (ResetDatabase)
        {
            await Fixture.ResetDatabaseAsync();
        }
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The <c>data</c> element, detached from its document so the document can be disposed —
    /// <see cref="JsonDocument"/> rents from an <c>ArrayPool</c> and leaking it is Sonar S2930.
    /// </summary>
    protected static async Task<JsonElement> ReadData(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    /// <summary>
    /// The machine-readable failure discriminator, which is what tells the module gate's 404 apart
    /// from a routing or lookup 404 — those carry none.
    /// </summary>
    protected static async Task<string?> ReadErrorCode(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return JsonSerializer.Deserialize<ApiResponse<object>>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.ErrorCode;
    }
}
