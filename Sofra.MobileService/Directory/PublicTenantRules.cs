using System.Text.RegularExpressions;

namespace Sofra.MobileService.Directory;

public static partial class PublicTenantRules
{
    private const int MaximumNameLength = 120;
    private const int MaximumSlugLength = 80;
    private const int MaximumContractVersions = 16;

    public static bool IsValid(PublicTenantDto tenant) =>
        tenant.Id != Guid.Empty
        && tenant.Slug.Length is > 0 and <= MaximumSlugLength && SlugPattern().IsMatch(tenant.Slug)
        && !string.IsNullOrWhiteSpace(tenant.Name) && tenant.Name.Length <= MaximumNameLength
        && DirectoryOrigin.IsCanonical(tenant.ApiOrigin) && DirectoryOrigin.IsCanonical(tenant.WebOrigin)
        && DirectoryOrigin.IsCanonical(tenant.ImageOrigin)
        && tenant.Status is "active" or "read-only" or "disabled"
        && tenant.ContractVersions.Count is > 0 and <= MaximumContractVersions
        && tenant.ContractVersions.All(version => version > 0)
        && tenant.ContractVersions.Distinct().Count() == tenant.ContractVersions.Count;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
