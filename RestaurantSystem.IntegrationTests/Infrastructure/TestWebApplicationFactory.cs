using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public TestWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // restaurantdb: set as an env var BEFORE Program.cs runs. Program.cs calls
        // .AddEnvironmentVariables() last in its config chain so this wins over JSON.
        // (The connection string is dynamic — comes from the per-test testcontainer
        // port — so it can't live in appsettings.Test.json.)
        Environment.SetEnvironmentVariable("ConnectionStrings__restaurantdb", _connectionString);
        // redis: the placeholder value lives in appsettings.Test.json
        // (ConnectionStrings:redis). Aspire's AddRedisDistributedCache needs a
        // non-empty value at startup, but the connection itself is never made —
        // the IDistributedCache registration is replaced below with the in-memory
        // implementation before any test code runs.

        builder.ConfigureTestServices(services =>
        {
            // Redis swap
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            // Auth overrides
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultForbidScheme = "Test";
            });

            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
                    .RequireAuthenticatedUser()
                    .Build();
            });
        });
    }
}
