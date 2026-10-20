using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

/// <summary>Native-only claim binding after Google's signature validation. No account/session writes.</summary>
public sealed class NativeGoogleIdentityVerifier(INativeGoogleSignatureVerifier signatures,
    IOptions<CustomerNativeSocialSettings> options, TimeProvider time) : INativeGoogleIdentityVerifier
{
    // Google protocol issuer identifiers, not application endpoints or configurable redirect URLs.
    public const string Issuer = "https://accounts.google.com";
    private const string LegacyIssuer = "accounts.google.com";
    public const int MaximumTokenLength = 16_384;

    public async Task<NativeSocialIdentity> VerifyAsync(string token, string expectedNonce,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.GoogleEnabled || !settings.IsValid())
            throw new NotFoundException("Native Google sign-in is unavailable.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaximumTokenLength || !ValidNonce(expectedNonce))
            throw Invalid();
        cancellationToken.ThrowIfCancellationRequested();
        var payload = await signatures.ValidateAsync(token, settings.GoogleClientIds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (payload is null || payload.Issuer is not (Issuer or LegacyIssuer)
            || string.IsNullOrWhiteSpace(payload.Subject) || payload.Subject.Length > 255
            || payload.Subject.Any(char.IsControl) || payload.Subject != payload.Subject.Trim()
            || payload.Audience is not string audience
            || !settings.GoogleClientIds.Contains(audience, StringComparer.Ordinal)
            || payload.ExpirationTimeSeconds is null || payload.ExpirationTimeSeconds <= now
            || payload.IssuedAtTimeSeconds is null || payload.IssuedAtTimeSeconds <= 0 || payload.IssuedAtTimeSeconds > now + 60
            || !ValidNonce(payload.Nonce)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedNonce), Encoding.ASCII.GetBytes(payload.Nonce))
            || !ValidEmail(payload.Email))
            throw Invalid();
        return new NativeSocialIdentity("google", Issuer, payload.Subject, audience,
            payload.Email, payload.EmailVerified);
    }

    private static bool ValidNonce(string? value) => value is { Length: >= 43 and <= 128 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool ValidEmail(string? value) => value is null || value.Length is >= 1 and <= 256
        && !value.Any(char.IsControl) && value == value.Trim() && new EmailAddressAttribute().IsValid(value);

    private static BadRequestException Invalid() => new("Provider sign-in could not be verified.");
}
