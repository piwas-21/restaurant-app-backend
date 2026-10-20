using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

public sealed class NativeGoogleIdentityVerifierTests
{
    private const string Client = "shared-native-client";
    private const string Token = "opaque-signed-provider-token";
    private static readonly string Nonce = new('a', 43);
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static GoogleJsonWebSignature.Payload Payload() => new()
    {
        Issuer = NativeGoogleIdentityVerifier.Issuer,
        Subject = "stable-provider-subject",
        Audience = Client,
        Nonce = Nonce,
        Email = "customer@example.test",
        EmailVerified = true,
        IssuedAtTimeSeconds = Now.ToUnixTimeSeconds() - 10,
        ExpirationTimeSeconds = Now.ToUnixTimeSeconds() + 3600
    };
    private static (NativeGoogleIdentityVerifier Verifier, Mock<INativeGoogleSignatureVerifier> Signature) Fixture(
        GoogleJsonWebSignature.Payload? payload, CustomerNativeSocialSettings? settings = null)
    {
        settings ??= new() { GoogleEnabled = true, GoogleClientIds = [Client] };
        var signature = new Mock<INativeGoogleSignatureVerifier>(MockBehavior.Strict);
        signature.Setup(service => service.ValidateAsync(Token, settings.GoogleClientIds, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        return (new(signature.Object, Options.Create(settings), new Clock()), signature);
    }

    [Theory]
    [InlineData("https://accounts.google.com")]
    [InlineData("accounts.google.com")]
    public async Task AcceptsOnlyValidatedBoundIdentityAndCanonicalizesTheGoogleIssuer(string issuer)
    {
        var payload = Payload(); payload.Issuer = issuer;
        var f = Fixture(payload);
        var result = await f.Verifier.VerifyAsync(Token, Nonce, default);
        Assert.Equal(new NativeSocialIdentity("google", "https://accounts.google.com", "stable-provider-subject",
            Client, "customer@example.test", true), result);
        f.Signature.Verify(service => service.ValidateAsync(Token, new[] { Client }, default), Times.Once);
    }

    [Fact]
    public async Task EmailChangesNeverChangeTheSubjectAndMissingOrUnverifiedContactRemainsDistinct()
    {
        var payload = Payload(); payload.Email = null; payload.EmailVerified = false;
        var first = await Fixture(payload).Verifier.VerifyAsync(Token, Nonce, default);
        payload.Email = "changed@example.test"; payload.EmailVerified = true;
        var second = await Fixture(payload).Verifier.VerifyAsync(Token, Nonce, default);
        Assert.Equal(first.Subject, second.Subject);
        Assert.Equal(first.Issuer, second.Issuer);
        Assert.Null(first.Email); Assert.False(first.EmailVerified);
        Assert.Equal("changed@example.test", second.Email); Assert.True(second.EmailVerified);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("multiple-audiences")]
    [InlineData("nonce")]
    [InlineData("missing-nonce")]
    [InlineData("missing-subject")]
    [InlineData("blank-subject")]
    [InlineData("long-subject")]
    [InlineData("control-subject")]
    [InlineData("padded-subject")]
    [InlineData("expired")]
    [InlineData("missing-expiry")]
    [InlineData("future-issue")]
    [InlineData("missing-issue")]
    [InlineData("zero-issue")]
    [InlineData("bad-email")]
    [InlineData("long-email")]
    [InlineData("padded-email")]
    [InlineData("control-email")]
    public async Task RefusesUnboundOrMalformedValidatedClaimsWithOneFixedMessage(string kind)
    {
        var payload = Payload();
        switch (kind)
        {
            case "issuer": payload.Issuer = "https://foreign.example"; break;
            case "audience": payload.Audience = "other-tenant-client"; break;
            case "multiple-audiences": payload.Audience = new[] { Client, "other-client" }; break;
            case "nonce": payload.Nonce = new string('b', 43); break;
            case "missing-nonce": payload.Nonce = null; break;
            case "missing-subject": payload.Subject = null; break;
            case "blank-subject": payload.Subject = " "; break;
            case "long-subject": payload.Subject = new string('s', 256); break;
            case "control-subject": payload.Subject = "private\0subject"; break;
            case "padded-subject": payload.Subject = " padded "; break;
            case "expired": payload.ExpirationTimeSeconds = Now.ToUnixTimeSeconds(); break;
            case "missing-expiry": payload.ExpirationTimeSeconds = null; break;
            case "future-issue": payload.IssuedAtTimeSeconds = Now.ToUnixTimeSeconds() + 61; break;
            case "missing-issue": payload.IssuedAtTimeSeconds = null; break;
            case "zero-issue": payload.IssuedAtTimeSeconds = 0; break;
            case "bad-email": payload.Email = "private-invalid-address"; break;
            case "long-email": payload.Email = new string('e', 250) + "@example.test"; break;
            case "padded-email": payload.Email = " customer@example.test "; break;
            case "control-email": payload.Email = "customer\n@example.test"; break;
        }
        var f = Fixture(kind == "signature" ? null : payload);
        var failure = await Assert.ThrowsAsync<BadRequestException>(() => f.Verifier.VerifyAsync(Token, Nonce, default));
        Assert.Equal("Provider sign-in could not be verified.", failure.Message);
        Assert.DoesNotContain(Token, failure.ToString());
    }

    [Theory]
    [InlineData(false, "shared-native-client")]
    [InlineData(true, "")]
    [InlineData(true, " padded-client ")]
    public async Task DisabledOrInvalidConfigurationDoesNotReachTheProvider(bool enabled, string client)
    {
        var f = Fixture(Payload(), new() { GoogleEnabled = enabled, GoogleClientIds = [client] });
        await Assert.ThrowsAsync<NotFoundException>(() => f.Verifier.VerifyAsync(Token, Nonce, default));
        f.Signature.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(42, false)]
    [InlineData(43, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task BoundsTheExpectedChallengeNonceBeforeProviderIo(int length, bool accepted)
    {
        var nonce = new string('a', length); var payload = Payload(); payload.Nonce = nonce;
        var f = Fixture(payload);
        if (accepted) Assert.Equal("stable-provider-subject", (await f.Verifier.VerifyAsync(Token, nonce, default)).Subject);
        else
        {
            await Assert.ThrowsAsync<BadRequestException>(() => f.Verifier.VerifyAsync(Token, nonce, default));
            f.Signature.VerifyNoOtherCalls();
        }
    }

    [Fact]
    public async Task CancellationWithdrawsValidatedClaimsBeforeReturningAnIdentity()
    {
        var f = Fixture(Payload()); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Verifier.VerifyAsync(Token, Nonce, cancellation.Token));
        f.Signature.VerifyNoOtherCalls();
    }
}
