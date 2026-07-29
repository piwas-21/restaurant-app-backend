using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Modules;

/// <summary>
/// Reads the tenant's module list once at startup and answers gate questions from it.
///
/// The rules, in precedence order — the first that matches wins:
///   1. enforcement off              -> everything on   (the ship-behind-a-flag switch)
///   2. no module list configured    -> everything on   (RUMI: no TENANT_MODULES exists)
///   3. the id is `core`             -> on              (fail open for core)
///   4. the id is not in the catalog -> OFF             (fail closed for unrecognised)
///   5. otherwise                    -> on iff listed
///
/// Rule 2 is the load-bearing one. The legacy RUMI install runs the main `deploy`
/// compose project rather than a per-tenant one, so the registry's `modules:` list for
/// it never reaches its container: `docker exec deploy-backend-1 env` has no
/// TENANT_MODULES. Reading "absent" as "nothing enabled" would take the whole app away
/// from the one live paying client.
/// </summary>
public sealed class TenantModules : ITenantModules
{
    private readonly HashSet<string> _enabled;

    public TenantModules(IOptions<ModuleSettings> options, ILogger<TenantModules> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value;
        var configured = (settings.Enabled ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var unknown = configured.Where(id => !ModuleIds.IsKnown(id)).ToArray();
        if (unknown.Length > 0)
        {
            // provision-tenant.sh already rejects an unknown id loudly at the seam, so
            // reaching here means someone hand-edited a tenant .env. Ignore the entry and
            // warn: hard-failing startup over a typo would take a live tenant down, and an
            // unknown id cannot enable anything anyway.
            logger.LogWarning(
                "Ignoring {Count} unrecognised module id(s) in Modules:Enabled: {Ids}",
                unknown.Length, string.Join(", ", unknown));
        }

        var known = configured.Where(ModuleIds.IsKnown).ToArray();

        // Rules 1 + 2. An empty list is NOT an empty allow-list — it means "unrestricted".
        IsEnforced = settings.Enforce && known.Length > 0;
        _enabled = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

        if (IsEnforced)
        {
            logger.LogInformation(
                "Module enforcement ON — enabled: {Modules}", string.Join(", ", EnabledModules));
        }
    }

    public bool IsEnforced { get; }

    public IReadOnlyList<string> EnabledModules =>
        IsEnforced
            ? ModuleIds.All.Where(IsEnabled).ToArray()
            : ModuleIds.All;

    public bool IsEnabled(string moduleId)
    {
        if (!IsEnforced) return true;                                                  // 1 + 2
        if (string.Equals(moduleId, ModuleIds.Core, StringComparison.OrdinalIgnoreCase)) return true; // 3
        if (!ModuleIds.IsKnown(moduleId)) return false;                                // 4
        return _enabled.Contains(moduleId);                                            // 5
    }
}
