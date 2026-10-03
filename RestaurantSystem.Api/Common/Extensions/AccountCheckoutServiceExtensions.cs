using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class AccountCheckoutServiceExtensions
{
    public const string ReceiptPolicyName = "account-payment-receipt";

    public static IServiceCollection AddAccountCheckoutServices(this IServiceCollection services)
    {
        services.AddOptions<AccountCheckoutSettings>().BindConfiguration(AccountCheckoutSettings.SectionName)
            .ValidateDataAnnotations().Validate(value => value.MaximumReconciliationBackoffSeconds
                >= value.ReconciliationIntervalSeconds, "Recovery backoff must cover the polling interval.")
            .ValidateOnStart();
        services.AddScoped<IAccountStripeCheckoutClient, AccountStripeCheckoutClient>();
        services.AddScoped<IAccountCheckoutJournalStore, AccountCheckoutJournalStore>();
        services.AddScoped<IAccountCheckoutStartService, AccountCheckoutStartService>();
        services.AddScoped<IAccountCheckoutCancelService, AccountCheckoutCancelService>();
        services.AddScoped<IAccountCheckoutStatusReader, AccountCheckoutStatusReader>();
        services.AddScoped<IAccountGuestCheckoutReader, AccountGuestCheckoutReader>();
        services.AddScoped<IAccountCheckoutEvidenceReader, AccountCheckoutEvidenceReader>();
        services.AddScoped<IAccountCheckoutEvidenceWriter, AccountCheckoutEvidenceWriter>();
        services.AddScoped<IAccountCheckoutLeaseStore, AccountCheckoutLeaseStore>();
        services.AddScoped<IAccountCheckoutCapturePoster, AccountCheckoutCapturePoster>();
        services.AddScoped<IAccountCheckoutReconciler, AccountCheckoutReconciler>();
        services.AddScoped<IAccountPaymentReceiptReader, AccountPaymentReceiptReader>();
        services.AddHostedService<AccountCheckoutReconciliationService>();
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(ReceiptPolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<TableGuestVisitSettings>>()
                        .Value.AccountReadsPerIpPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })));
        return services;
    }
}
