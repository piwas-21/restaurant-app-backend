using Microsoft.Extensions.Options;
using Sofra.MobileService.Links;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

public sealed class MobileAssociationValidationTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("disabled", true)]
    [InlineData("links-disabled", false)]
    [InlineData("empty-apple", false)]
    [InlineData("invalid-apple", false)]
    [InlineData("newline-apple", false)]
    [InlineData("empty-android", false)]
    [InlineData("invalid-android", false)]
    [InlineData("newline-android", false)]
    [InlineData("no-fingerprint", false)]
    [InlineData("invalid-fingerprint", false)]
    [InlineData("newline-fingerprint", false)]
    [InlineData("duplicate-fingerprint", false)]
    [InlineData("too-many-fingerprints", false)]
    public void RequiresExplicitValidPublisherAndSigningMetadataWhenEnabled(string scenario, bool valid)
    {
        var settings = new MobileAssociationSettings
        {
            Enabled = scenario != "disabled",
            AppleApplicationId = scenario switch { "empty-apple" => "", "invalid-apple" => "com.example.app", "newline-apple" => MobileAssociationFixture.AppleId + "\n", _ => MobileAssociationFixture.AppleId },
            AndroidPackage = scenario switch { "empty-android" => "", "invalid-android" => "not a package", "newline-android" => MobileAssociationFixture.AndroidPackage + "\n", _ => MobileAssociationFixture.AndroidPackage },
            AndroidSha256Fingerprints = scenario switch
            {
                "newline-fingerprint" => [MobileAssociationFixture.Fingerprint + "\n"],
                "no-fingerprint" => [],
                "invalid-fingerprint" => ["not-a-certificate"],
                "duplicate-fingerprint" => [MobileAssociationFixture.Fingerprint, MobileAssociationFixture.Fingerprint],
                "too-many-fingerprints" => Enumerable.Range(0, 5).Select(value => string.Join(':', Enumerable.Repeat(value.ToString("X2"), 32))).ToArray(),
                _ => [MobileAssociationFixture.Fingerprint]
            }
        };
        var links = Options.Create(new MobileLinkSettings { Enabled = scenario != "links-disabled", Origin = "https://links.example" });
        Assert.Equal(valid, new MobileAssociationSettingsValidator(links).Validate(null, settings).Succeeded);
    }

    [Fact]
    public void DefaultDisabledConfigurationHasNoPublisherIdentityAndNoDocument()
    {
        var settings = new MobileAssociationSettings();
        var links = Options.Create(new MobileLinkSettings());
        Assert.True(new MobileAssociationSettingsValidator(links).Validate(null, settings).Succeeded);
        var handler = new GetMobileAssociationsQueryHandler(links, Options.Create(settings));
        Assert.Null(handler.Apple("links.example"));
        Assert.Null(handler.Android("links.example"));
        Assert.Empty(settings.AppleApplicationId);
        Assert.Empty(settings.AndroidPackage);
        Assert.Empty(settings.AndroidSha256Fingerprints);
    }

    [Fact]
    public void FingerprintRotationCopiesConfigurationAndReturnsReadOnlyMetadata()
    {
        var fingerprints = new List<string> { MobileAssociationFixture.Fingerprint };
        var settings = new MobileAssociationSettings
        {
            Enabled = true,
            AppleApplicationId = MobileAssociationFixture.AppleId,
            AndroidPackage = MobileAssociationFixture.AndroidPackage,
            AndroidSha256Fingerprints = fingerprints
        };
        var links = Options.Create(new MobileLinkSettings { Enabled = true, Origin = "https://links.example" });
        var handler = new GetMobileAssociationsQueryHandler(links, Options.Create(settings));
        fingerprints.Clear();
        var target = Assert.Single(handler.Android("links.example")!).Target;
        Assert.Equal([MobileAssociationFixture.Fingerprint], target.Fingerprints);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)target.Fingerprints).Clear());
    }
}
