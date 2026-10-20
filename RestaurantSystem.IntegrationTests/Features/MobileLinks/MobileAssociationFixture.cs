using Sofra.MobileService.Links;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

internal static class MobileAssociationFixture
{
    internal const string AppleId = "ABCDE12345.com.example.customer";
    internal const string AndroidPackage = "com.example.customer";
    internal static string Fingerprint => string.Join(':', Enumerable.Repeat("AA", 32));
    internal static MobileAssociationSettings Settings(bool enabled = true) => new()
    {
        Enabled = enabled,
        AppleApplicationId = AppleId,
        AndroidPackage = AndroidPackage,
        AndroidSha256Fingerprints = [Fingerprint]
    };
}
