using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class AccountCheckoutWebhookServiceExtensions
{
    public const string PolicyName = "account-checkout-stripe-webhook";

    public static IServiceCollection AddAccountCheckoutWebhookServices(this IServiceCollection services)
    {
        services.AddOptions<AccountCheckoutWebhookSettings>()
            .BindConfiguration(AccountCheckoutWebhookSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IAccountCheckoutWebhookService, AccountCheckoutWebhookService>();
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices
                        .GetRequiredService<IOptions<AccountCheckoutWebhookSettings>>()
                        .Value.MaximumRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })));
        return services;
    }
}
