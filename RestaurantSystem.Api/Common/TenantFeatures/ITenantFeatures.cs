namespace RestaurantSystem.Api.Common.TenantFeatures;

/// <summary>
/// Process-lifetime tenant feature switches. Rollout changes require a container recreate.
/// </summary>
public interface ITenantFeatures
{
    /// <summary>Whether the Server Workspace V2 entry seam is enabled.</summary>
    bool ServerWorkspaceV2 { get; }

    /// <summary>Whether staff bill views present the single visit account.</summary>
    bool TableAccountV1 { get; }

    /// <summary>Whether authorized native amendment quote and commit endpoints are enabled.</summary>
    bool OrderAmendmentsV1 { get; }

    /// <summary>Whether scoped guest visit admission and rounds are enabled.</summary>
    bool TableGuestVisitsV1 { get; }

    /// <summary>Whether exact visit payment quotes and reservations are enabled.</summary>
    bool TableAccountPaymentsV1 { get; }

    /// <summary>Whether server writes enforce each product's minimum selected sauces.</summary>
    bool EnforceSauceMinimum { get; }

    /// <summary>Whether admins may materialize or import option sets into tenant menu rows.</summary>
    bool OptionSetMaterializationEnabled { get; }
}
