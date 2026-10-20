using Microsoft.Extensions.Options;
using Sofra.MobileService.Directory;

namespace Sofra.MobileService.Links;

public sealed class MobileLinkSettingsValidator : IValidateOptions<MobileLinkSettings>
{
    public ValidateOptionsResult Validate(string? name, MobileLinkSettings settings)
    {
        if (!settings.Enabled && string.IsNullOrEmpty(settings.Origin)) return ValidateOptionsResult.Success;
        return DirectoryOrigin.IsCanonical(settings.Origin)
            && Uri.TryCreate(settings.Origin, UriKind.Absolute, out var origin) && origin.IsDefaultPort
            ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Mobile link origin must be canonical HTTPS.");
    }
}
