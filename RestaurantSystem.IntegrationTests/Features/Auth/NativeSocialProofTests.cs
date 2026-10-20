using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

public sealed class NativeSocialProofTests
{
    [Fact]
    public void UsesTheIndependentSha256GoldenAndNeverStoresARawProofDigestAsTheProof()
    {
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", NativeSocialChallengeProof.Hash("abc")); // pragma: allowlist secret (public SHA-256 abc test vector)
        var proof = NativeSocialChallengeProof.Create(); var other = NativeSocialChallengeProof.Create();
        Assert.Equal(43, proof.Length); Assert.NotEqual(proof, other);
        Assert.True(NativeSocialChallengeProof.IsEncodedProof(proof));
        var hash = NativeSocialChallengeProof.Hash(proof);
        Assert.Equal(64, hash.Length); Assert.DoesNotContain(proof, hash);
        Assert.True(NativeSocialChallengeProof.Matches(proof, hash));
        Assert.False(NativeSocialChallengeProof.Matches(other, hash));
        Assert.False(NativeSocialChallengeProof.Matches(hash, hash));
        Assert.False(NativeSocialChallengeProof.Matches(proof, "invalid-digest"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("a proof containing whitespace is not accepted")]
    public void RejectsMalformedProofsBeforeComparison(string? proof)
    {
        Assert.False(NativeSocialChallengeProof.IsEncodedProof(proof));
        Assert.False(NativeSocialChallengeProof.Matches(proof, new string('A', 64)));
    }

    [Fact]
    public void ConfigurationIsInertByDefaultAndRefusesDuplicateOrEmptySharedAudiences()
    {
        var settings = new CustomerNativeSocialSettings();
        Assert.False(settings.GoogleEnabled); Assert.True(settings.IsValid());
        settings.GoogleEnabled = true; Assert.False(settings.IsValid());
        settings.GoogleClientIds = ["shared-client"]; Assert.True(settings.IsValid());
        settings.GoogleClientIds = ["shared-client", "shared-client"]; Assert.False(settings.IsValid());
        settings.GoogleClientIds = [""]; Assert.False(settings.IsValid());
        settings.GoogleClientIds = [" foreign-client "]; Assert.False(settings.IsValid());
        settings.GoogleClientIds = [new string('a', 257)]; Assert.False(settings.IsValid());
        settings.GoogleClientIds = ["shared-client\n"]; Assert.False(settings.IsValid());
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(180, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void BoundsChallengeLifetime(int seconds, bool valid)
    {
        var settings = new CustomerNativeSocialSettings
        { GoogleEnabled = true, GoogleClientIds = ["shared-client"], ChallengeLifetimeSeconds = seconds };
        Assert.Equal(valid, settings.IsValid());
    }

    [Fact]
    public async Task RealSignatureAdapterHonorsAnAlreadyCancelledRequestWithoutProviderIo()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NativeGoogleSignatureVerifier()
            .ValidateAsync("private-token", new[] { "shared-client" }, cancellation.Token));
    }
}
