using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, string> _settings;
    private readonly Action<IServiceCollection>? _configureTestServices;
    private readonly bool _disableApplicationHostedServices;

    /// <param name="settings">
    /// Extra configuration keys, per-instance (a process-wide environment variable would
    /// race across xUnit's parallel runs). Used by tests that need the host built with a
    /// different configuration than appsettings.json — e.g. module enforcement on.
    ///
    /// Added as the LAST configuration source rather than via UseSetting: UseSetting lands in
    /// host configuration, which the appsettings*.json sources are layered on top of, so it
    /// silently loses to any key those files already define. (It works for
    /// ConnectionStrings:restaurantdb above only because appsettings.Test.json declares
    /// `redis` and not `restaurantdb`.)
    /// </param>
    /// <param name="configureTestServices">
    /// Applied LAST, after this class's own overrides, so a test can swap a real service for a
    /// double — e.g. a recording <c>IEmailService</c>, which is the only way to assert that a mail
    /// was actually sent rather than merely recorded as claimed.
    /// </param>
    /// <param name="disableApplicationHostedServices">
    /// Drops this application's own <see cref="IHostedService"/> registrations (the cleanup,
    /// retention and reconciliation sweeps). Set ONLY for the long-lived shared host of
    /// <see cref="DatabaseFixture.SharedFactory"/>: a per-test host lives under a second, so its
    /// 5-minute and 1-minute timers never fire, but a host that lives for the whole run would
    /// sweep DURING an unrelated test and delete rows that test had just seeded. No test loses
    /// coverage — every background service is exercised by tests that construct it directly
    /// (e.g. <c>ReservationRetentionServiceTests</c>), never through the hosted registration.
    /// </param>
    public TestWebApplicationFactory(
        string connectionString,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? configureTestServices = null,
        bool disableApplicationHostedServices = false)
    {
        _connectionString = connectionString;
        _settings = settings ?? new Dictionary<string, string>();
        _configureTestServices = configureTestServices;
        _disableApplicationHostedServices = disableApplicationHostedServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        if (_settings.Count > 0)
        {
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(
                    _settings.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value))));
        }

        // restaurantdb: inject per-instance via UseSetting (NOT
        // Environment.SetEnvironmentVariable — that's process-wide and
        // xUnit parallel test runs would race on the shared variable).
        // The connection string is dynamic per testcontainer instance.
        builder.UseSetting("ConnectionStrings:restaurantdb", _connectionString);
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

            if (_disableApplicationHostedServices)
            {
                // This application's own sweeps only — never the framework's own hosted services
                // (removing those would stop the test server from ever starting).
                var appAssembly = typeof(Program).Assembly;
                foreach (var descriptor in services
                             .Where(d => d.ServiceType == typeof(IHostedService)
                                         && d.ImplementationType?.Assembly == appAssembly)
                             .ToList())
                {
                    services.Remove(descriptor);
                }
            }

            _configureTestServices?.Invoke(services);
        });
    }
}
