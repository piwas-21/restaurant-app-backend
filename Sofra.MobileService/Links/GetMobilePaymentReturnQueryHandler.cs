using Microsoft.Extensions.Options;
using Sofra.MobileService.Directory;

namespace Sofra.MobileService.Links;

/// <summary>Public navigation only: no customer credentials, tenant database or settlement service.</summary>
public sealed class GetMobilePaymentReturnQueryHandler(IOptions<MobileLinkSettings> settings,
    IGetMobileDirectoryQueryHandler directory) : IGetMobilePaymentReturnQueryHandler
{
    public bool Handle(MobilePaymentReturnQuery query)
    {
        if (!settings.Value.Enabled || !Uri.TryCreate(settings.Value.Origin, UriKind.Absolute, out var origin)
            || !string.Equals(query.RequestAuthority, origin.Authority, StringComparison.OrdinalIgnoreCase)
            || query.QueryKeyCount != 1 || query.OperationValueCount != 1
            || !CanonicalReference(query.OrderReference) || !CanonicalReference(query.Operation)) return false;
        return directory.Handle().Tenants.Any(tenant => tenant.Slug == query.Slug
            && tenant.Status is "active" or "read-only" && tenant.ContractVersions.Contains(1));
    }

    private static bool CanonicalReference(string? value) =>
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && id.ToString("D") == value;
}
