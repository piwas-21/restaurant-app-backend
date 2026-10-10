using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Partner;

public static class TenantBrandingExtensions
{
    public static IServiceCollection AddTenantBranding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PartnerSettings>().Bind(configuration.GetSection(PartnerSettings.SectionName))
            .Validate(settings => string.IsNullOrWhiteSpace(settings.RuntimeUrl)
                || (Uri.TryCreate(settings.RuntimeUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
                    && settings.RequestTimeoutSeconds > 0
                    && System.Text.RegularExpressions.Regex.IsMatch(settings.TenantSlug, "^[a-z0-9][a-z0-9-]{0,62}$",
                        System.Text.RegularExpressions.RegexOptions.NonBacktracking, TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))
                    && settings.RefreshSeconds > 0 && settings.MaxStaleSeconds >= settings.RefreshSeconds), "Invalid tenant branding configuration")
            .ValidateOnStart();
        services.AddHttpClient(nameof(TenantBranding))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ITenantPartner, TenantPartner>();
        services.AddSingleton<ITenantBranding, TenantBranding>();
        return services;
    }
}
