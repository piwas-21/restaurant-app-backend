using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

// No [Collection] here on purpose: the concrete classes carry their own [Collection("Database
// Lane N")], which is what lets them run in parallel lanes (see DatabaseCollections.cs). An
// attribute here would be inherited by every subclass and put them all back in one serial
// collection. A NEW DB-backed test class must therefore declare a lane attribute itself —
// without one xUnit cannot supply the DatabaseFixture and the class fails loudly at run time.
public abstract class IntegrationTestBase : IAsyncLifetime
{
    /// <summary>
    /// Whether a concrete test class overrides <see cref="ConfigureTestServices"/> — cached, because
    /// the answer is per-type and the question is asked once per test.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> OverridesDiCache = new();

    protected readonly DatabaseFixture DatabaseFixture;
    protected TestWebApplicationFactory Factory = null!;
    protected HttpClient Client = null!;

    /// <summary>The host this instance owns and must dispose. Null when it borrows the shared one.</summary>
    private TestWebApplicationFactory? _ownedFactory;

    protected IntegrationTestBase(DatabaseFixture databaseFixture)
    {
        DatabaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new StringEnumConverterFactory() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Opt out of the shared host. Override to <c>true</c> ONLY for a class whose subject is
    /// in-memory host state that the per-test Respawn wipe cannot reset — a rate-limiter window,
    /// a "seeded once" singleton flag, an SSE client registry. Everything else is isolated by the
    /// database reset alone and should share, because a private host costs ~0.4s per test.
    /// <para>
    /// A class that overrides <see cref="ConfigureTestServices"/> gets its own host automatically;
    /// it does not need this.
    /// </para>
    /// </summary>
    protected virtual bool RequiresIsolatedHost => false;

    public async Task InitializeAsync()
    {
        // Own host only when this class customises DI or explicitly needs isolated host state;
        // otherwise borrow the collection-wide one (see DatabaseFixture.SharedFactory).
        if (RequiresIsolatedHost || OverridesConfigureTestServices(GetType()))
        {
            _ownedFactory = new TestWebApplicationFactory(
                DatabaseFixture.ConnectionString, configureTestServices: ConfigureTestServices);
            Factory = _ownedFactory;
        }
        else
        {
            Factory = DatabaseFixture.SharedFactory;
        }

        Client = Factory.CreateClient();

        Client.DefaultRequestHeaders.Accept.Clear();

        Client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));


        // Wipe + reseed only when the database is not already sitting in exactly the state this
        // test would recreate. A test that only read left the previous test's canonical seed
        // untouched, and re-deleting rows just to re-insert the identical ones costs ~47 ms.
        // Classes that seed extra data of their own are excluded: their rows are not canonical, and
        // an instance of such a class typically remembers ids its own seeding produced.
        var usesDefaultSeedOnly = !OverridesSeedTestData(GetType());

        if (usesDefaultSeedOnly && await DatabaseFixture.IsSeedIntactAsync())
        {
            return;
        }

        await DatabaseFixture.ResetDatabaseAsync();
        await SeedTestData();

        if (usesDefaultSeedOnly)
        {
            await DatabaseFixture.MarkSeededAsync();
        }
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        // Never the shared host: it outlives this test and belongs to the collection fixture.
        _ownedFactory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether a concrete test class overrides <see cref="SeedTestData"/> — cached like
    /// <see cref="OverridesConfigureTestServices"/>, and asked once per test.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> OverridesSeedCache = new();

    private static bool OverridesSeedTestData(Type type) =>
        OverridesSeedCache.GetOrAdd(type, static t =>
            t.GetMethod(
                nameof(SeedTestData),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
             ?.DeclaringType != typeof(IntegrationTestBase));

    private static bool OverridesConfigureTestServices(Type type) =>
        OverridesDiCache.GetOrAdd(type, static t =>
            t.GetMethod(
                nameof(ConfigureTestServices),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
             ?.DeclaringType != typeof(IntegrationTestBase));

    /// <summary>
    /// Last word on the test host's DI. Empty by default — a test class overrides it to replace a
    /// service with a double.
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected virtual async Task SeedTestData()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TestDataSeeder.SeedBasicDataAsync(context);
    }

    /// <summary>
    /// Authenticates as Admin. Clears the role and anonymous headers for the same reason
    /// <see cref="AuthenticateAsUser"/> does — and this one was the exception: it used to set
    /// <c>X-Test-Admin</c> without clearing <see cref="TestAuthHandler.AnonymousHeader"/>, so
    /// <c>AuthenticateAsAnonymous()</c> followed by this stayed anonymous. A test that asserts a
    /// guest is refused and then checks staff still get through would have had its control silently
    /// run as a guest too, passing while proving nothing.
    /// </summary>
    protected void AuthenticateAsAdmin()
    {
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.RoleHeader);
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.AnonymousHeader);
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
    }

    /// <summary>
    /// Authenticates as the default Customer. Clears the role and anonymous headers too: a test
    /// that switched identity mid-run (AuthenticateAsRole(...) then this) would otherwise stay
    /// staff and quietly assert the wrong side of the authorization rule it exists to pin.
    /// </summary>
    protected void AuthenticateAsUser()
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.RoleHeader);
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.AnonymousHeader);
    }

    /// <summary>
    /// Authenticates as a non-admin back-of-house role (Cashier / KitchenStaff / Server),
    /// so authorization rules that turn on <c>ICurrentUserService.IsStaff</c> can be pinned
    /// for every staff role rather than for Admin alone.
    /// </summary>
    protected void AuthenticateAsRole(UserRole role)
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.AnonymousHeader);
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.RoleHeader);
        Client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role.ToString());
    }

    /// <summary>
    /// The printer fleet's device key — what a paired printer-app sends. NOT a user identity:
    /// these endpoints have no user, and <c>ApiKeyAuthFilter</c> is the only thing in front of
    /// them.
    /// <para>
    /// It exists because #475 made that filter FAIL CLOSED. The suite used to reach the device
    /// endpoints with no key at all, since an unconfigured key opened the door — so every test
    /// that touched them was passing through an unauthenticated one and proving nothing about
    /// the guard. The value matches <c>PrinterSettings:ApiKey</c> in appsettings.Test.json.
    /// </para>
    /// </summary>
    protected void AuthenticateAsDevice()
    {
        Client.DefaultRequestHeaders.Remove(DeviceApiKeyHeader);
        Client.DefaultRequestHeaders.Add(DeviceApiKeyHeader, TestPrinterApiKey);
    }

    // The HEADER NAME, not a credential — detect-secrets flags it only because the identifier
    // contains "ApiKey". Marked inline rather than baselined: a baseline entry is pinned to a
    // LINE NUMBER, and this file is edited often.
    protected const string DeviceApiKeyHeader = "X-Api-Key"; // pragma: allowlist secret

    /// <summary>
    /// Mirrors <c>PrinterSettings:ApiKey</c> in appsettings.Test.json. A fixed test value, not a
    /// credential — it authenticates nothing outside this suite's own host.
    /// </summary>
    protected const string TestPrinterApiKey = "integration-test-printer-api-key"; // pragma: allowlist secret

    /// <summary>
    /// Sends requests with no credentials at all. Note that merely clearing the
    /// Authorization header does NOT do this — <see cref="TestAuthHandler"/> authenticates
    /// every request by default, so a guest scenario needs this explicit opt-in.
    /// </summary>
    protected void AuthenticateAsAnonymous()
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.RoleHeader);
        Client.DefaultRequestHeaders.Authorization = null;
        Client.DefaultRequestHeaders.Remove(TestAuthHandler.AnonymousHeader);
        Client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
    }

    protected void AuthenticateAsTestUser()
    {
        // The TestAuthHandler will provide the user claims
        // We just need to ensure our created user ID matches what the basket service expects
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Add("Authorization", "Test");
    }

    // Helper methods for JSON serialization/deserialization with correct options
    protected async Task<T?> GetFromJsonAsync<T>(string requestUri)
    {
        var response = await Client.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    protected async Task<HttpResponseMessage> PostAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await Client.PostAsync(requestUri, content);
    }

    protected async Task<HttpResponseMessage> PutAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await Client.PutAsync(requestUri, content);
    }

    protected async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
