using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// BACKEND-NOTES §4.1. <c>AppleLoginCommandHandler</c> used to call
/// <c>JwtSecurityTokenHandler.ReadToken</c>, which DECODES without verifying, and its audience
/// check was commented out "to allow easier testing if config is missing". An unsigned JWT
/// carrying any <c>email</c> claim therefore logged the sender into that account — including
/// accounts opened with a password.
///
/// <para>
/// Every test here fails against that old behaviour: each token below is one <c>ReadToken</c>
/// would have accepted. No network: Apple's key set arrives through
/// <see cref="IAppleSigningKeyProvider"/>, which is the seam the production code was built around.
/// </para>
/// </summary>
public class AppleIdentityTokenVerifierTests
{
    private const string VictimEmail = "victim@example.test";

    [Fact]
    public async Task ValidToken_IsAccepted_AndCarriesItsClaims()
    {
        var result = await ValidateAsync(AppleTestTokens.Valid(VictimEmail));

        result.IsValid.Should().BeTrue(result.Error);
        result.Identity.Should().NotBeNull();
        result.Identity!.Email.Should().Be(VictimEmail);
        result.Identity.Subject.Should().Be(AppleTestTokens.Subject);
        result.Identity.EmailVerified.Should().BeTrue("Apple sends email_verified as the STRING \"true\"");
    }

    /// <summary>The reported takeover, verbatim: no signature at all.</summary>
    [Fact]
    public async Task UnsignedToken_IsRejected()
    {
        var result = await ValidateAsync(AppleTestTokens.Unsigned(VictimEmail));

        result.IsValid.Should().BeFalse("an alg:none token must never authenticate anyone");
        result.Identity.Should().BeNull();
    }

    [Fact]
    public async Task TokenSignedByAnotherKey_IsRejected()
    {
        var result = await ValidateAsync(AppleTestTokens.SignedByAnotherKey(VictimEmail));

        result.IsValid.Should().BeFalse("only Apple's published keys may sign an Apple identity token");
    }

    [Fact]
    public async Task WrongIssuer_IsRejected()
    {
        var result = await ValidateAsync(AppleTestTokens.WrongIssuer(VictimEmail));

        result.IsValid.Should().BeFalse("iss must be https://appleid.apple.com");
    }

    [Fact]
    public async Task WrongAudience_IsRejected()
    {
        var result = await ValidateAsync(AppleTestTokens.WrongAudience(VictimEmail));

        result.IsValid.Should().BeFalse("a token minted for another app must not open an account here");
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var result = await ValidateAsync(AppleTestTokens.Expired(VictimEmail));

        result.IsValid.Should().BeFalse("exp is enforced with a small skew, not ignored");
    }

    /// <summary>
    /// Fail CLOSED. This is the exact condition the old comment used to skip the check for.
    /// </summary>
    [Fact]
    public async Task MissingClientIdConfiguration_RefusesInsteadOfSkippingTheCheck()
    {
        var settings = new AppleAuthSettings { ClientIds = new List<string>() };

        var result = await ValidateAsync(AppleTestTokens.Valid(VictimEmail), settings);

        result.IsValid.Should().BeFalse("an unconfigured deployment must refuse, never trust");
        result.IsUnavailable.Should().BeTrue("the refusal is ours, so the caller gets a 503, not a 400");
    }

    /// <summary>The legacy single-value key must keep working — it is what a box may already set.</summary>
    [Fact]
    public async Task LegacySingleClientIdConfiguration_IsHonoured()
    {
        var settings = new AppleAuthSettings
        {
            ClientIds = new List<string>(),
            ClientId = AppleTestTokens.ClientId,
        };

        var result = await ValidateAsync(AppleTestTokens.Valid(VictimEmail), settings);

        result.IsValid.Should().BeTrue(result.Error);
    }

    /// <summary>
    /// Apple rotates its signing keys, so an unknown <c>kid</c> is normal traffic, not an attack:
    /// the validator must re-fetch once and then accept.
    /// </summary>
    [Fact]
    public async Task UnknownKeyId_RefetchesOnce_ThenAccepts()
    {
        var keys = new FakeAppleSigningKeyProvider(
            new[] { AppleTestTokens.UnrelatedPublicKey },
            new[] { AppleTestTokens.PublicKey, AppleTestTokens.UnrelatedPublicKey });

        var result = await Verifier(keys).ValidateAsync(AppleTestTokens.Valid(VictimEmail), CancellationToken.None);

        result.IsValid.Should().BeTrue(result.Error);
        keys.ForcedRefreshCount.Should().Be(1, "one refresh per unknown kid, not one per request");
    }

    /// <summary>
    /// And the negative of the rotation branch: a forged signature must not buy an endless
    /// stream of refreshes, or an anonymous caller could drive our traffic to Apple.
    /// </summary>
    [Fact]
    public async Task ForgedSignatureWithAKnownKeyId_DoesNotTriggerARefresh()
    {
        var keys = new FakeAppleSigningKeyProvider(new[] { AppleTestTokens.PublicKey });

        var result = await Verifier(keys)
            .ValidateAsync(AppleTestTokens.SignedByAnotherKey(VictimEmail), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        keys.ForcedRefreshCount.Should().Be(0);
    }

    [Fact]
    public async Task EmptyToken_IsRejected()
    {
        var result = await ValidateAsync(string.Empty);

        result.IsValid.Should().BeFalse();
    }

    private static Task<AppleTokenValidationResult> ValidateAsync(
        string idToken, AppleAuthSettings? settings = null)
    {
        var provider = new FakeAppleSigningKeyProvider(new[] { AppleTestTokens.PublicKey });
        return Verifier(provider, settings).ValidateAsync(idToken, CancellationToken.None);
    }

    private static AppleIdentityTokenVerifier Verifier(
        IAppleSigningKeyProvider keyProvider, AppleAuthSettings? settings = null) =>
        new(keyProvider,
            Options.Create(settings ?? Settings()),
            NullLogger<AppleIdentityTokenVerifier>.Instance);

    private static AppleAuthSettings Settings() => new()
    {
        ClientIds = new List<string> { AppleTestTokens.ClientId },
        Issuer = AppleTestTokens.Issuer,
    };
}
