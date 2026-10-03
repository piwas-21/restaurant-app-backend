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
        TableAccountV1 = options.Value.TableAccountV1;
        OrderAmendmentsV1 = options.Value.OrderAmendmentsV1;
        TableGuestVisitsV1 = options.Value.TableGuestVisitsV1;
        TableVisitReadinessV1 = options.Value.TableVisitReadinessV1;
        TableAccountPaymentsV1 = options.Value.TableAccountPaymentsV1;
        TableGuestAccountPaymentsV1 = options.Value.TableGuestAccountPaymentsV1;
        if (TableVisitReadinessV1 && (!ServerWorkspaceV2 || !TableGuestVisitsV1))
        {
            throw new InvalidOperationException(
                "TableVisitReadinessV1 requires ServerWorkspaceV2 and TableGuestVisitsV1.");
        }
        EnforceSauceMinimum = options.Value.EnforceSauceMinimum;
        OptionSetMaterializationEnabled = options.Value.OptionSetMaterializationEnabled;
    }

    public bool ServerWorkspaceV2 { get; }

    public bool TableAccountV1 { get; }

    public bool OrderAmendmentsV1 { get; }

    public bool TableGuestVisitsV1 { get; }

    public bool TableVisitReadinessV1 { get; }

    public bool TableAccountPaymentsV1 { get; }

    public bool TableGuestAccountPaymentsV1 { get; }

    public bool EnforceSauceMinimum { get; }

    public bool OptionSetMaterializationEnabled { get; }
}
