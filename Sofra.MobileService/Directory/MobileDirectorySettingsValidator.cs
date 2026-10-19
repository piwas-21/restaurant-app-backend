using Microsoft.Extensions.Options;

namespace Sofra.MobileService.Directory;

public sealed class MobileDirectorySettingsValidator : IValidateOptions<MobileDirectorySettings>
{
    private const int MaximumRevisionLength = 128;

    public ValidateOptionsResult Validate(string? name, MobileDirectorySettings settings)
    {
        if (settings.Environment is not ("staging" or "production")
            || string.IsNullOrWhiteSpace(settings.Revision) || settings.Revision.Length > MaximumRevisionLength
            || settings.LifetimeSeconds is < MobileDirectorySettings.MinimumLifetimeSeconds
                or > MobileDirectorySettings.MaximumLifetimeSeconds
            || settings.Tenants.Count > MobileDirectorySettings.MaximumTenants
            || settings.Tenants.Any(tenant => !PublicTenantRules.IsValid(tenant)))
            return ValidateOptionsResult.Fail("Mobile directory configuration is invalid.");
        var unique = settings.Tenants.Select(tenant => tenant.Id).Distinct().Count() == settings.Tenants.Count
            && settings.Tenants.Select(tenant => tenant.Slug).Distinct(StringComparer.Ordinal).Count() == settings.Tenants.Count
            && settings.Tenants.Select(tenant => tenant.ApiOrigin).Distinct(StringComparer.Ordinal).Count() == settings.Tenants.Count;
        return unique ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Mobile directory tenant identities must be unique.");
    }
}
