using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class CustomerNativeAccountExtensions
{
    public static IServiceCollection AddCustomerNativeAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CustomerNativeAccountSettings>()
            .Bind(configuration.GetSection(CustomerNativeAccountSettings.SectionName), options => options.ErrorOnUnknownConfiguration = true)
            .Validate(settings => settings.IsValid(), "CustomerNativeAccounts settings are invalid.")
            .ValidateOnStart();
        services.AddScoped<ICustomerNativeAccountLinks, CustomerNativeAccountLinks>();
        services.AddScoped<ICustomerNativeAccountMail, CustomerNativeAccountMail>();
        return services;
    }
}
