namespace RestaurantSystem.Api.Common.TenantFeatures;

/// <summary>
/// Process-lifetime tenant feature switches. Rollout changes require a container recreate.
/// </summary>
public interface ITenantFeatures
{
    /// <summary>Whether the Server Workspace V2 entry seam is enabled.</summary>
    bool ServerWorkspaceV2 { get; }
}
