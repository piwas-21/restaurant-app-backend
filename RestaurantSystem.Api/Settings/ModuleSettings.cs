namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Which product modules this tenant instance runs (sofra ADR-010 / S11).
/// Bound from the "Modules" configuration section; per-tenant provisioning injects
/// Modules__Enabled and Modules__Enforce from the deploy repo's tenants/registry.yml.
/// </summary>
public class ModuleSettings
{
    /// <summary>
    /// Comma-separated module ids in the registry's own grammar, e.g.
    /// "core,kitchen-board,cashier". EMPTY MEANS UNRESTRICTED: the legacy RUMI
    /// install runs the main compose project and has no TENANT_MODULES at all,
    /// so an absent list must keep every feature rather than remove them all.
    /// </summary>
    public string Enabled { get; set; } = string.Empty;

    /// <summary>
    /// Master switch. False (the default) disables gating entirely whatever
    /// <see cref="Enabled"/> says, so this ships inert and is turned on per tenant.
    /// </summary>
    public bool Enforce { get; set; }
}
