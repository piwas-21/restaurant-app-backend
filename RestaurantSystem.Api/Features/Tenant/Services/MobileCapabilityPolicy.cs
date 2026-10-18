using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Tenant.Services;

/// <summary>Native rollout is an explicit allow-list in addition to purchased modules and backend flags.</summary>
public static class MobileCapabilityPolicy
{
    private static readonly Dictionary<string, string> RequiredModules = new Dictionary<string, string>
    {
        ["menu"] = ModuleIds.Core,
        ["ordering"] = ModuleIds.Core,
        ["accounts"] = ModuleIds.Core,
        ["reservations"] = ModuleIds.Reservations,
        ["loyalty"] = ModuleIds.Loyalty,
        ["table-visits"] = ModuleIds.Server,
        ["table-payments"] = ModuleIds.OnlinePayments,
        ["online-payments"] = ModuleIds.OnlinePayments
    };

    public static bool IsKnown(string capability) => RequiredModules.ContainsKey(capability);

    private const string Enabled = "enabled";
    private const string Disabled = "disabled";
    private const string Unavailable = "unavailable";
    private const string Unknown = "unknown";

    public static IReadOnlyDictionary<string, string> Resolve(CustomerMobileSettings settings,
        ITenantModules modules, ITenantFeatures features, bool hasCurrency, bool supported) =>
        RequiredModules.ToDictionary(pair => pair.Key,
            pair => ResolveCapability(pair.Key, pair.Value, settings, modules, features, hasCurrency, supported),
            StringComparer.Ordinal);

    private static string ResolveCapability(string capability, string module, CustomerMobileSettings settings,
        ITenantModules modules, ITenantFeatures features, bool hasCurrency, bool supported)
    {
        if (!supported || settings.Lifecycle == Disabled) return Unavailable;
        if (!settings.EnabledCapabilities.Contains(capability, StringComparer.Ordinal)
            || !modules.IsEnabled(module)) return Disabled;
        if (capability != "menu" && (settings.Lifecycle != "active" || !hasCurrency)) return Unavailable;
        return capability switch
        {
            "table-visits" => features.TableGuestVisitsV1 ? Enabled : Disabled,
            "table-payments" => GuestPaymentsConfigured(modules, features) ? Unknown : Disabled,
            // Merchant/provider availability must be independently proved before offering payment.
            "online-payments" => Unknown,
            _ => Enabled
        };
    }

    private static bool GuestPaymentsConfigured(ITenantModules modules, ITenantFeatures features) =>
        features.TableGuestVisitsV1 && features.TableGuestAccountPaymentsV1
        && features.TableAccountPaymentsV1 && modules.IsEnabled(ModuleIds.Server);
}
