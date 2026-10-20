using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Features.Tenant.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class CustomerNativeSocialExtensions
{
    public static IServiceCollection AddCustomerNativeSocial(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CustomerNativeSocialSettings>()
            .Bind(configuration.GetSection(CustomerNativeSocialSettings.SectionName), options => options.ErrorOnUnknownConfiguration = true)
            .Validate(settings => settings.IsValid(), "CustomerNativeSocial settings are invalid.")
            .ValidateOnStart();
        services.AddSingleton<INativeGoogleSignatureVerifier, NativeGoogleSignatureVerifier>();
        services.AddScoped<INativeGoogleIdentityVerifier, NativeGoogleIdentityVerifier>();
        services.AddScoped<INativeSocialAccess, NativeSocialAccess>();
        services.AddScoped<INativeSocialChallenges, NativeSocialChallenges>();
        services.AddScoped<INativeSocialAccounts, NativeSocialAccounts>();
        services.AddScoped<INativeSocialCompletion, NativeSocialCompletion>();
        services.AddScoped<ICustomerNativeAuthenticationCapabilities, CustomerNativeAuthenticationCapabilities>();
        return services;
    }
}
