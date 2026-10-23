using Sofra.MobileService.Directory;

namespace Sofra.MobileService.Links;

internal sealed record MobileReturnReference(string Slug, string Reference, string? Operation,
    int QueryKeys, int OperationValues, string Authority);

internal static class MobileReturnNavigation
{
    internal static bool Allowed(MobileLinkSettings settings, IGetMobileDirectoryQueryHandler directory,
        MobileReturnReference request)
    {
        if (!settings.Enabled || !Uri.TryCreate(settings.Origin, UriKind.Absolute, out var origin)
            || !string.Equals(request.Authority, origin.Authority, StringComparison.OrdinalIgnoreCase)
            || request.QueryKeys != 1 || request.OperationValues != 1 || !Canonical(request.Reference) || !Canonical(request.Operation)) return false;
        return directory.Handle().Tenants.Any(tenant => tenant.Slug == request.Slug
            && tenant.Status is "active" or "read-only" && tenant.ContractVersions.Contains(1));
    }
    private static bool Canonical(string? value) =>
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && id.ToString("D") == value;
}
