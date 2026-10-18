using Microsoft.Extensions.Options;
using Sofra.MobileService.Directory;

namespace RestaurantSystem.IntegrationTests.Features.MobileDirectory;

public sealed class MobileDirectoryValidationTests
{
    private static ValidateOptionsResult Validate(MobileDirectorySettings settings) =>
        new MobileDirectorySettingsValidator().Validate(null, settings);

    [Fact]
    public void ReviewedPublicConfigurationAcceptsTwoDistinctTenantsAndIntentionalEmptyDirectory()
    {
        Assert.True(Validate(MobileDirectoryFixture.Settings()).Succeeded);
        Assert.True(Validate(MobileDirectoryFixture.Settings([])).Succeeded);
    }

    [Theory]
    [InlineData("https://alpha.example", true)]
    [InlineData("https://alpha.example/", false)]
    [InlineData("https://alpha.example:443", false)]
    [InlineData("https://alpha.example/api", false)]
    [InlineData("https://user@alpha.example", false)]
    [InlineData("https://alpha.example?redirect=other", false)]
    [InlineData("https://alpha.example#fragment", false)]
    [InlineData("http://alpha.example", false)]
    [InlineData("https://ALPHA.example", false)]
    public void OriginsMustMatchTheNativeCanonicalHttpsContract(string value, bool expected) =>
        Assert.Equal(expected, DirectoryOrigin.IsCanonical(value));

    [Fact]
    public void DuplicateIdSlugOrApiOriginCannotAmbiguouslyResolveCredentials()
    {
        var first = MobileDirectoryFixture.Alpha;
        var second = MobileDirectoryFixture.Beta;
        foreach (var invalid in new[] { second with { Id = first.Id }, second with { Slug = first.Slug }, second with { ApiOrigin = first.ApiOrigin } })
            Assert.True(Validate(MobileDirectoryFixture.Settings([first, invalid])).Failed);
    }

    [Theory]
    [InlineData("unknown", 3600, "reviewed-1")]
    [InlineData("staging", 0, "reviewed-1")]
    [InlineData("production", 86401, "reviewed-1")]
    [InlineData("staging", 3600, "")]
    public void IncompatibleEnvelopeIsRefused(string environment, int lifetime, string revision) =>
        Assert.True(Validate(MobileDirectoryFixture.Settings(environment: environment, lifetime: lifetime, revision: revision)).Failed);

    [Fact]
    public void InvalidTenantStatusIdentityAndCompatibilityAreRefused()
    {
        var valid = MobileDirectoryFixture.Alpha;
        foreach (var invalid in new[] { valid with { Id = Guid.Empty }, valid with { Slug = "../alpha" },
            valid with { Name = " " }, valid with { Status = "future-enabled" }, valid with { ContractVersions = [] },
            valid with { ContractVersions = [1, 1] }, valid with { ContractVersions = [0] } })
            Assert.True(Validate(MobileDirectoryFixture.Settings([invalid])).Failed);
    }
}
