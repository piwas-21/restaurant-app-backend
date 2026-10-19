using RestaurantSystem.Api.Features.Basket.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public static class CustomerBasketOperationRegistration
{
    // Keep the configured add budget shared by native edits and both add URLs.
    public const string AddPolicyName = "customer-basket-add";
    public static IServiceCollection AddCustomerBasketOperations(this IServiceCollection services)
    {
        services.AddOptions<BasketAddRateLimitSettings>().BindConfiguration(BasketAddRateLimitSettings.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<ICustomerBasketOperationService, CustomerBasketOperationService>();
        services.AddScoped<ICustomerBasketItemMutationService, CustomerBasketItemMutationService>();
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(AddPolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<BasketAddRateLimitSettings>>()
                        .Value.AttemptsPerIpPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })));
        return services;
    }
}
