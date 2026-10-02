using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Api;

public static class TenantBridgeServices
{
    public static IServiceCollection AddTenantBridge(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TenantBridgeSettings>().Bind(configuration.GetSection(TenantBridgeSettings.Section))
            .Validate(settings => settings.IsValid(), "Tenant bridge requires a reviewed exact store, tenant origin, catalogue mapping and enrollment boundary.")
            .Validate<IOptions<SandboxConsoleSettings>, IOptions<UberWebhookSettings>>((settings, console, webhook)
                => !settings.Enabled || console.Value.Enabled && webhook.Value.StoreIds.Length == 1
                    && webhook.Value.StoreIds[0] == settings.Store.StoreId,
                "Tenant bridge requires the existing sandbox-only provider connection for exactly its configured store.")
            .ValidateOnStart();
        services.AddScoped<IChannelImportJobs, PostgresChannelImportJobs>();
        services.AddScoped<IUberOrderNormalizer, UberOrderNormalizer>();
        services.AddScoped<ITenantImportProcessor, TenantImportProcessor>();
        services.AddScoped<ITenantOrderClient, TenantOrderClient>();
        services.AddScoped<IChannelObservationJobs, PostgresChannelObservationJobs>();
        services.AddScoped<ITenantObservationClient, TenantObservationClient>();
        services.AddScoped<ITenantObservationProcessor, TenantObservationProcessor>();
        services.AddHostedService<TenantObservationWorker>();
        services.AddScoped<ITenantDecisionClient, TenantDecisionClient>();
        services.AddScoped<IChannelOrderLinks, PostgresChannelOrderLinks>();
        services.AddScoped<ITenantDecisionProcessor, TenantDecisionProcessor>();
        services.AddHttpClient<ITenantChannelTransport, TenantChannelTransport>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHostedService<TenantImportWorker>();
        services.AddHostedService<TenantDecisionWorker>();
        return services;
    }
}
