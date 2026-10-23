using Sofra.MobileService.Directory;

namespace Sofra.MobileService.Links;

internal static class MobileReturnNavigation
{
    internal static bool Allowed(MobileLinkSettings settings, IGetMobileDirectoryQueryHandler directory,
        string slug, string reference, string? operation, int queryKeys, int operationValues, string authority)
    {
        if (!settings.Enabled || !Uri.TryCreate(settings.Origin, UriKind.Absolute, out var origin)
            || !string.Equals(authority, origin.Authority, StringComparison.OrdinalIgnoreCase)
            || queryKeys != 1 || operationValues != 1 || !Canonical(reference) || !Canonical(operation)) return false;
        return directory.Handle().Tenants.Any(tenant => tenant.Slug == slug
            && tenant.Status is "active" or "read-only" && tenant.ContractVersions.Contains(1));
    }
    private static bool Canonical(string? value) =>
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && id.ToString("D") == value;
}
