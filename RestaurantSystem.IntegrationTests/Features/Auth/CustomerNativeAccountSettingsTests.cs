using RestaurantSystem.Api.Features.Auth.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

public sealed class CustomerNativeAccountSettingsTests
{
    [Fact]
    public void Missing_configuration_is_disabled_and_has_no_routing_identity()
    {
        var settings = new CustomerNativeAccountSettings();
        Assert.False(settings.Enabled);
        Assert.True(settings.IsValid());
        Assert.Empty(settings.LinkOrigin);
        Assert.Empty(settings.TenantSlug);
    }

    [Theory]
    [InlineData("https://links.example", "alpha", 60, true)]
    [InlineData("https://links.example/", "alpha", 60, false)]
    [InlineData("http://links.example", "alpha", 60, false)]
    [InlineData("https://links.example?next=other", "alpha", 60, false)]
    [InlineData("https://links.example#other", "alpha", 60, false)]
    [InlineData("https://links.example:444", "alpha", 60, false)]
    [InlineData("https://user@links.example", "alpha", 60, false)]
    [InlineData("https://links.example", "Alpha", 60, false)]
    [InlineData("https://links.example", "alpha--beta", 60, false)]
    [InlineData("https://links.example", "alpha", 0, false)]
    [InlineData("https://links.example", "alpha", 121, false)]
    public void Only_reviewed_canonical_routing_and_bounded_lifetime_are_accepted(string origin, string slug, int minutes, bool valid) =>
        Assert.Equal(valid, new CustomerNativeAccountSettings { Enabled = true, LinkOrigin = origin, TenantSlug = slug, LinkLifetimeMinutes = minutes }.IsValid());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("raw+identity/token=")]
    [InlineData("private@example.test")]
    public void Unbounded_or_non_url_safe_proofs_are_refused(string? proof) => Assert.False(CustomerNativeAccountLinks.ValidProof(proof));

    [Fact]
    public void Proof_bounds_are_enforced_in_both_directions()
    {
        Assert.False(CustomerNativeAccountLinks.ValidProof(new string('a', 95)));
        Assert.True(CustomerNativeAccountLinks.ValidProof(new string('a', 96)));
        Assert.True(CustomerNativeAccountLinks.ValidProof(new string('a', 4096)));
        Assert.False(CustomerNativeAccountLinks.ValidProof(new string('a', 4097)));
    }
}
