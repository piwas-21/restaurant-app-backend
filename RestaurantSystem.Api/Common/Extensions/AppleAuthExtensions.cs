using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

/// <summary>
/// Registers Apple identity-token verification (BACKEND-NOTES §4.1). Always registered, and
/// inert-but-refusing when <c>Authentication:Apple:ClientIds</c> is empty — the point being
/// that an unconfigured deployment rejects apple-login instead of trusting it.
/// </summary>
public static class AppleAuthExtensions
{
    public static IServiceCollection AddAppleAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AppleAuthSettings>(configuration.GetSection(AppleAuthSettings.SectionName));

        var settings = configuration.GetSection(AppleAuthSettings.SectionName).Get<AppleAuthSettings>()
                       ?? new AppleAuthSettings();

        // Typed client, with a timeout: a login must fail fast when Apple is slow rather than
        // hold a request thread until the default 100s.
        services.AddHttpClient<IAppleJwksFetcher, AppleJwksFetcher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.JwksTimeoutSeconds, 1, 60));
        });

        // Singleton: the key cache is the whole reason this type exists.
        services.AddSingleton<IAppleSigningKeyProvider, AppleSigningKeyProvider>();
        services.AddScoped<IAppleIdentityTokenVerifier, AppleIdentityTokenVerifier>();

        return services;
    }
}
