using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RestaurantSystem.Channels.Api;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Tests;

public abstract class ConsoleFixture(GatewayFixture fixture) : IAsyncLifetime
{
    protected const string Origin = "https://sandbox.example";
    protected const string AccessKey = "public-fixture-key-not-a-secret-0000000000000000000000";
    protected GatewayFixture Database => fixture;
    protected FakeUberSandboxClient Provider { get; } = new();
    protected WebApplicationFactory<Program> Host { get; private set; } = new();
    protected HttpClient Client { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await using var db = new NpgsqlConnection(fixture.ConnectionString);
        await db.OpenAsync();
        await using var clean = new NpgsqlCommand("TRUNCATE channel_console_sessions, channel_authorization_states, channel_sandbox_tokens, channel_sandbox_order_actions", db);
        await clean.ExecuteNonQueryAsync();
        Host.Dispose(); Client.Dispose();
        Host = fixture.Host().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SandboxConsole:Enabled"] = "true",
                ["SandboxConsole:PublicBaseUrl"] = Origin,
                ["SandboxConsole:AuthBaseUrl"] = "https://sandbox-login.uber.com/",
                ["SandboxConsole:ApiBaseUrl"] = "https://test-api.uber.com/",
                ["SandboxConsole:OwnerAccessHash"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AccessKey))),
                ["SandboxConsole:EncryptionKey"] = Convert.ToBase64String(new byte[32]),
            }));
            builder.ConfigureTestServices(services => services.AddSingleton<IUberSandboxClient>(Provider));
        });
        Client = NewClient();
    }

    protected HttpClient NewClient()
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Origin), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", Origin);
        return client;
    }

    protected async Task Login(HttpClient? client = null)
    {
        using var response = await (client ?? Client).PostAsJsonAsync("/api/sandbox/auth/login", new { accessKey = AccessKey });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    protected async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Host.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    protected async Task<string> Session()
        => await InScope(async services =>
        {
            var crypto = services.GetRequiredService<ISandboxCrypto>(); var hash = crypto.Hash(Guid.NewGuid().ToString());
            await services.GetRequiredService<IConsoleRepository>().CreateSession(hash, DateTimeOffset.UtcNow.AddMinutes(10), default);
            return hash;
        });

    protected async Task<string> SeedOrder()
    {
        var order = Guid.NewGuid().ToString();
        await InScope(async services => await services.GetRequiredService<IWebhookInbox>().Receive(new(GatewayFixture.ClientId,
            Guid.NewGuid().ToString(), "orders.notification", GatewayFixture.StoreId, order, 1427343990, new string('0', 64), DateTimeOffset.UtcNow), default));
        return order;
    }

    protected static async Task<JsonElement> Json(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>());

    public Task DisposeAsync() { Client.Dispose(); Host.Dispose(); return Task.CompletedTask; }
}
