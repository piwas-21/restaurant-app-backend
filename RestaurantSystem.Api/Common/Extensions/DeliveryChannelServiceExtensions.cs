using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Features.DeliveryChannels.Management;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Common.Extensions;

public static class DeliveryChannelServiceExtensions
{
    public const string MachineIngressPolicy = "DeliveryChannelMachineIngress";
    public const string MachineCataloguePolicy = "DeliveryChannelMachineCatalogue";
    public const string HumanManagementPolicy = "DeliveryChannelHumanManagement";

    public static IServiceCollection AddDeliveryChannelServices(this IServiceCollection services)
    {
        services.AddAuthorization(options => options.AddPolicy(MachineIngressPolicy, policy => policy
            .AddAuthenticationSchemes(ApiTokenDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ApiTokenDefaults.AuthMethodClaimType, ApiTokenDefaults.ApiTokenAuthMethod)
            .RequireClaim(ApiTokenDefaults.ScopeClaimType, ApiTokenScopes.ChannelOrdersWrite)));
        services.AddAuthorization(options => options.AddPolicy(MachineCataloguePolicy, policy => policy
            .AddAuthenticationSchemes(ApiTokenDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ApiTokenDefaults.AuthMethodClaimType, ApiTokenDefaults.ApiTokenAuthMethod)
            .RequireClaim(ApiTokenDefaults.ScopeClaimType, ApiTokenScopes.ChannelCatalogueRead)));
        services.AddAuthorization(options => options.AddPolicy(HumanManagementPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole("Admin")
            .RequireAssertion(context => context.User.FindFirstValue(ApiTokenDefaults.AuthMethodClaimType)
                != ApiTokenDefaults.ApiTokenAuthMethod)));
        services.AddOptions<DeliveryChannelSettings>().BindConfiguration(DeliveryChannelSettings.SectionName)
            .Validate(settings => !settings.Enabled || settings.Stores.Count > 0
                && settings.Stores.All(store => !string.IsNullOrWhiteSpace(store.Provider)
                    && !string.IsNullOrWhiteSpace(store.StoreId) && store.Currency.Length == 3
                    && (!settings.SandboxOnly || store.IsSandbox)), "Enabled channels require explicit store/currency bindings.")
            .Validate(settings => settings.DecisionLeaseSeconds is >= 60 and <= 300
                && settings.DecisionRetrySeconds is >= 10 and <= 300
                && settings.DecisionClockToleranceSeconds is >= 0 and <= 60,
                "Channel decision lease, retry and clock durations must remain within operational bounds.")
            .ValidateOnStart();
        services.AddOptions<DeliveryChannelManagementSettings>().BindConfiguration(DeliveryChannelManagementSettings.SectionName)
            .Validate(settings => settings.IsValid(), "Delivery channel management requires a secure gateway origin and server-only credential.")
            .Validate<IOptions<DeliveryChannelSettings>>((management, channel) => !management.Enabled
                || channel.Value.Enabled && channel.Value.SandboxOnly && channel.Value.Stores.Count == 1
                    && channel.Value.Stores[0].Provider == "uber-eats" && channel.Value.Stores[0].IsSandbox
                    && Guid.TryParse(channel.Value.Stores[0].StoreId, out var storeId) && storeId != Guid.Empty,
                "Tenant delivery management requires the exact enabled Uber sandbox store binding.")
            .ValidateOnStart();
        services.AddHttpClient<IDeliveryChannelManagementClient, DeliveryChannelManagementClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<DeliveryChannelManagementSettings>>().Value;
            if (!settings.Enabled) return;
            client.BaseAddress = new Uri(settings.GatewayBaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IExternalOrderImporter, ExternalOrderImporter>();
        services.AddScoped<IChannelAvailabilityReader, ChannelAvailabilityReader>();
        services.AddScoped<IChannelCatalogueReader, ChannelCatalogueReader>();
        services.AddScoped<IChannelCatalogueInventoryReader, ChannelCatalogueInventoryReader>();
        services.AddScoped<IChannelDecisionQueue, ChannelDecisionQueue>();
        services.AddScoped<IChannelOrderObserver, ChannelOrderObserver>();
        services.AddScoped<IChannelDecisionDelivery, ChannelDecisionDelivery>();
        return services;
    }
}
