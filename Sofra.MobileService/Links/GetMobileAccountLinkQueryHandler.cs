using Microsoft.Extensions.Options;
using Sofra.MobileService.Directory;

namespace Sofra.MobileService.Links;

/// <summary>The grant stays in the caller's fragment; this service sees only public navigation.</summary>
public sealed class GetMobileAccountLinkQueryHandler(IOptions<MobileLinkSettings> settings,
    IGetMobileDirectoryQueryHandler directory) : IGetMobileAccountLinkQueryHandler
{
    public bool Handle(MobileAccountLinkQuery query) => settings.Value.Enabled
        && Uri.TryCreate(settings.Value.Origin, UriKind.Absolute, out var origin)
        && string.Equals(query.RequestAuthority, origin.Authority, StringComparison.OrdinalIgnoreCase)
        && query.QueryKeyCount == 0 && query.Action is "verify" or "recover"
        && directory.Handle().Tenants.Any(tenant => tenant.Slug == query.Slug
            && tenant.Status == "active" && tenant.ContractVersions.Contains(1));
}
