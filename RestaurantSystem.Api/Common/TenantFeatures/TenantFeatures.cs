using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.TenantFeatures;

/// <summary>
/// Reads tenant feature switches once at startup and exposes the validated values to API code.
/// </summary>
public sealed class TenantFeatures : ITenantFeatures
{
    public TenantFeatures(IOptions<TenantFeatureSettings> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ServerWorkspaceV2 = options.Value.ServerWorkspaceV2;
        EnforceSauceMinimum = options.Value.EnforceSauceMinimum;
        OptionSetMaterializationEnabled = options.Value.OptionSetMaterializationEnabled;
    }

    public bool ServerWorkspaceV2 { get; }

    public bool EnforceSauceMinimum { get; }

    public bool OptionSetMaterializationEnabled { get; }
}
