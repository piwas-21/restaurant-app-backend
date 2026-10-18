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

    public static IReadOnlyDictionary<string, string> Resolve(CustomerMobileSettings settings,
        ITenantModules modules, ITenantFeatures features, bool hasCurrency, bool supported)
    {
        return RequiredModules.ToDictionary(pair => pair.Key, pair =>
        {
            if (!supported || settings.Lifecycle == "disabled") return "unavailable";
            if (!settings.EnabledCapabilities.Contains(pair.Key, StringComparer.Ordinal)
                || !modules.IsEnabled(pair.Value)) return "disabled";
            if (pair.Key != "menu" && (settings.Lifecycle != "active" || !hasCurrency)) return "unavailable";
            if (pair.Key == "table-visits" && !features.TableGuestVisitsV1) return "disabled";
            if (pair.Key == "table-payments"
                && (!features.TableGuestVisitsV1 || !features.TableGuestAccountPaymentsV1
                    || !features.TableAccountPaymentsV1 || !modules.IsEnabled(ModuleIds.Server))) return "disabled";
            // Merchant/provider availability is independently probed before offering online payment.
            if (pair.Key is "online-payments" or "table-payments") return "unknown";
            return "enabled";
        }, StringComparer.Ordinal);
    }
}
