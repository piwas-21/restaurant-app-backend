using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Api;

public static class SandboxConsoleServices
{
    public static IServiceCollection AddSandboxConsole(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SandboxConsoleSettings>().Bind(configuration.GetSection(SandboxConsoleSettings.Section))
            .Validate(s => s.IsValid(), "Sandbox console requires secure origins and configured access/encryption keys.")
            .Validate<IOptions<UberWebhookSettings>>((s, w) => !s.Enabled || w.Value.IsConfigured && w.Value.StoreIds.Length == 1,
                "Sandbox console requires exactly one configured sandbox store.").ValidateOnStart();
        services.AddScoped<IConsoleRepository, PostgresConsoleRepository>();
        services.AddSingleton<ISandboxCrypto, SandboxCrypto>();
        services.AddSingleton<TokenRefreshLock>();
        services.AddScoped<ISandboxSessions, SandboxSessions>();
        services.AddScoped<ISandboxTokens, SandboxTokens>();
        services.AddScoped<ISandboxConnection, SandboxConnection>();
        services.AddScoped<ISandboxAuthorization, SandboxAuthorization>();
        services.AddScoped<ISandboxMenu, SandboxMenu>();
        services.AddScoped<ISandboxOrders, SandboxOrders>();
        services.AddScoped<IChannelCommandHandler<ConsoleCommand, System.Text.Json.JsonElement>, ConsoleCommandHandler>();
        services.AddScoped<IChannelQueryHandler<ConsoleQuery, System.Text.Json.JsonElement>, ConsoleQueryHandler>();
        services.AddHttpClient<IUberSandboxClient, UberSandboxClient>((provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<SandboxConsoleSettings>>().Value.HttpTimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddRateLimiter(o =>
        {
            o.AddFixedWindowLimiter("sandbox-login", limiter => { limiter.PermitLimit = 20; limiter.Window = TimeSpan.FromMinutes(1); });
            o.AddFixedWindowLimiter("sandbox-console", limiter => { limiter.PermitLimit = 120; limiter.Window = TimeSpan.FromMinutes(1); });
        });
        return services;
    }
}
