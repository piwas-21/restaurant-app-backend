using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sofra.MobileService.Links;

public sealed partial class MobileAssociationSettingsValidator(IOptions<MobileLinkSettings> links)
    : IValidateOptions<MobileAssociationSettings>
{
    private const int MaximumFingerprints = 4;
    private const int MaximumAppleApplicationLength = 266;
    private const int MaximumAndroidPackageLength = 255;

    public ValidateOptionsResult Validate(string? name, MobileAssociationSettings settings)
    {
        if (!settings.Enabled) return ValidateOptionsResult.Success;
        if (!links.Value.Enabled || !new MobileLinkSettingsValidator().Validate(null, links.Value).Succeeded
            || string.IsNullOrEmpty(settings.AppleApplicationId) || settings.AppleApplicationId.Length > MaximumAppleApplicationLength
            || !AppleApplication().IsMatch(settings.AppleApplicationId)
            || string.IsNullOrEmpty(settings.AndroidPackage) || settings.AndroidPackage.Length > MaximumAndroidPackageLength
            || !AndroidPackage().IsMatch(settings.AndroidPackage)
            || settings.AndroidSha256Fingerprints is not { Count: >= 1 and <= MaximumFingerprints }
            || settings.AndroidSha256Fingerprints.Any(value => string.IsNullOrEmpty(value) || !Fingerprint().IsMatch(value))
            || settings.AndroidSha256Fingerprints.Distinct(StringComparer.Ordinal).Count() != settings.AndroidSha256Fingerprints.Count)
            return ValidateOptionsResult.Fail("Mobile associations require an enabled link host and reviewed app/signing identities.");
        return ValidateOptionsResult.Success;
    }

    [GeneratedRegex(@"\A[A-Z0-9]{10}\.[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\z", RegexOptions.NonBacktracking)]
    private static partial Regex AppleApplication();
    [GeneratedRegex(@"\A[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+\z", RegexOptions.NonBacktracking)]
    private static partial Regex AndroidPackage();
    [GeneratedRegex(@"\A(?:[A-F0-9]{2}:){31}[A-F0-9]{2}\z", RegexOptions.NonBacktracking)]
    private static partial Regex Fingerprint();
}
