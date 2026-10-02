using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Common.Extensions;

public static class DeliveryChannelServiceExtensions
{
    public const string MachineIngressPolicy = "DeliveryChannelMachineIngress";

    public static IServiceCollection AddDeliveryChannelServices(this IServiceCollection services)
    {
        services.AddAuthorization(options => options.AddPolicy(MachineIngressPolicy, policy => policy
            .AddAuthenticationSchemes(ApiTokenDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ApiTokenDefaults.AuthMethodClaimType, ApiTokenDefaults.ApiTokenAuthMethod)
            .RequireClaim(ApiTokenDefaults.ScopeClaimType, ApiTokenScopes.ChannelOrdersWrite)));
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
        services.AddScoped<IExternalOrderImporter, ExternalOrderImporter>();
        services.AddScoped<IChannelDecisionQueue, ChannelDecisionQueue>();
        services.AddScoped<IChannelDecisionDelivery, ChannelDecisionDelivery>();
        return services;
    }
}
