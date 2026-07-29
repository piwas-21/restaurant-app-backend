namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Which product modules this tenant instance runs (sofra ADR-010 / S11).
///
/// Bound from the "Modules" configuration section. The deploy repo's tenant compose
/// template is what maps a registry `modules:` list onto Modules__Enabled; both
/// defaults here mean UNRESTRICTED, so an instance nobody has configured — every
/// instance, until that mapping ships and a tenant opts in — behaves exactly as it did
/// before this existed.
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
