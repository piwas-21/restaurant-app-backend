using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Api;

public static class TenantManagementGatewayServices
{
    public static IServiceCollection AddTenantManagementGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TenantManagementGatewaySettings>().Bind(configuration.GetSection(TenantManagementGatewaySettings.Section))
            .Validate(settings => settings.IsValid(), "Tenant management requires a valid server-only gateway credential and secure callback origins.")
            .Validate<IOptions<SandboxConsoleSettings>>((management, console) => !management.Enabled
                || console.Value.Enabled && management.CallbackUrl == console.Value.PublicBaseUrl.TrimEnd('/') + SandboxConsoleSettings.CallbackPath,
                "Tenant OAuth must use Uber's existing registered gateway callback URL.")
            .Validate<IOptions<TenantBridgeSettings>>((management, bridge) => !management.Enabled
                || bridge.Value.Enabled && management.ReturnUrl == bridge.Value.Store.BaseUrl.TrimEnd('/') + TenantManagementGatewaySettings.ReturnPath
                    && bridge.Value.Store.StoreId != Guid.Empty,
                "Tenant management must bind to the explicitly enabled tenant bridge.")
            .Validate<IOptions<UberWebhookSettings>>((management, webhook) => !management.Enabled
                || webhook.Value.IsConfigured && webhook.Value.StoreIds.Length == 1,
                "Tenant management requires exactly one configured Uber sandbox store.")
            .ValidateOnStart();
        services.AddScoped<ITenantManagementOAuth, TenantManagementOAuth>();
        services.AddScoped<IChannelManagementAudit, PostgresChannelManagementAudit>();
        services.AddScoped<ITenantChannelManagementOperations, TenantChannelManagementOperations>();
        return services;
    }
}
