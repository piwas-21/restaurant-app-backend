namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Runtime rollout switches for tenant-scoped product surfaces.
/// </summary>
public sealed class TenantFeatureSettings
{
    /// <summary>The configuration section bound from the tenant environment.</summary>
    public const string SectionName = "TenantFeatures";

    /// <summary>
    /// Enables the isolated Server Workspace V2 entry seam. This is deliberately not an
    /// authorization or module entitlement; it only selects the UI implementation.
    /// </summary>
    public bool ServerWorkspaceV2 { get; set; }

    /// <summary>
    /// Requires configured minimum sauce choices at write time. Defaults off for existing guest,
    /// staff and mobile clients until each tenant's payload compatibility is verified.
    /// </summary>
    public bool EnforceSauceMinimum { get; set; }

    /// <summary>Enables explicit option-set materialization and catalogue import commits for this tenant.</summary>
    public bool OptionSetMaterializationEnabled { get; set; }
}
