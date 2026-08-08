using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly DatabaseFixture DatabaseFixture;
    protected TestWebApplicationFactory Factory = null!;
    protected HttpClient Client = null!;

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

    public async Task InitializeAsync()
    {
        // Create factory and client after DatabaseFixture is initialized
        Factory = new TestWebApplicationFactory(DatabaseFixture.ConnectionString);
        Client = Factory.CreateClient();

        Client.DefaultRequestHeaders.Accept.Clear();

        Client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));


        await DatabaseFixture.ResetDatabaseAsync();
        await SeedTestData();
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        Factory?.Dispose();
        return Task.CompletedTask;
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
