using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Features.Auth.Interfaces;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// A stand-in for Apple: one RSA key pair whose public half is handed to the validator through
/// <see cref="FakeAppleSigningKeyProvider"/>, plus factories for every token shape the
/// verification path has to judge — including the UNSIGNED one that BACKEND-NOTES §4.1 reports
/// as an account takeover.
/// </summary>
internal static class AppleTestTokens
{
    public const string Issuer = "https://appleid.apple.com";
    public const string ClientId = "ch.rumirestaurant.app.test";
    public const string Subject = "001234.fedcba9876543210.1234";
    public const string KeyId = "apple-test-key";

    private static readonly RSA AppleRsa = RSA.Create(2048);
    private static readonly RSA ImpostorRsa = RSA.Create(2048);

    /// <summary>The key the validator must trust — as Apple's JWKS would publish it.</summary>
    public static SecurityKey PublicKey { get; } = new RsaSecurityKey(AppleRsa.ExportParameters(false)) { KeyId = KeyId };

    /// <summary>A trusted key that signs nothing here, used to prove an unknown <c>kid</c> path.</summary>
    public static SecurityKey UnrelatedPublicKey { get; } =
        new RsaSecurityKey(ImpostorRsa.ExportParameters(false)) { KeyId = "some-rotated-away-key" };

    public static string Valid(string email, bool emailVerified = true) =>
        Signed(AppleRsa, KeyId, Issuer, ClientId, email, DateTime.UtcNow.AddMinutes(10), emailVerified);

    public static string SignedByAnotherKey(string email) =>
        Signed(ImpostorRsa, KeyId, Issuer, ClientId, email, DateTime.UtcNow.AddMinutes(10));

    public static string WrongIssuer(string email) =>
        Signed(AppleRsa, KeyId, "https://evil.example.com", ClientId, email, DateTime.UtcNow.AddMinutes(10));

    public static string WrongAudience(string email) =>
        Signed(AppleRsa, KeyId, Issuer, "com.someone.else", email, DateTime.UtcNow.AddMinutes(10));

    public static string Expired(string email) =>
        Signed(AppleRsa, KeyId, Issuer, ClientId, email, DateTime.UtcNow.AddMinutes(-30));

    public static string SignedWithUnknownKeyId(string email) =>
        Signed(AppleRsa, "a-kid-nobody-has-seen", Issuer, ClientId, email, DateTime.UtcNow.AddMinutes(10));

    /// <summary>
    /// The attack from the report: a well-formed JWT with <c>alg: none</c>, no signature and any
    /// <c>email</c> claim the attacker likes. <c>JwtSecurityTokenHandler.ReadToken</c> accepted it.
    /// </summary>
    public static string Unsigned(string email)
    {
        var header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64Url(JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["iss"] = Issuer,
            ["aud"] = ClientId,
            ["sub"] = Subject,
            ["email"] = email,
            ["email_verified"] = "true",
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        }));

        return $"{header}.{payload}.";
    }

    private static string Signed(
        RSA rsa, string keyId, string issuer, string audience, string email, DateTime expires,
        bool emailVerified = true)
    {
        var signingKey = new RsaSecurityKey(rsa) { KeyId = keyId };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            // A token minted in the past must still be VALID at its own issue time, otherwise the
            // expired-token test would be proving `nbf` rather than `exp`.
            NotBefore = expires.AddMinutes(-20),
            IssuedAt = expires.AddMinutes(-20),
            Expires = expires,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = Subject,
                ["email"] = email,
                ["email_verified"] = emailVerified ? "true" : "false",
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string Base64Url(string json) =>
        Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
}

/// <summary>
/// Serves whatever keys a test wants, and records how often a forced refresh was asked for —
/// which is how the key-rotation branch is proven without touching Apple.
/// </summary>
internal sealed class FakeAppleSigningKeyProvider : IAppleSigningKeyProvider
{
    private readonly IReadOnlyCollection<SecurityKey> _initialKeys;
    private readonly IReadOnlyCollection<SecurityKey> _keysAfterRefresh;

    public FakeAppleSigningKeyProvider(
        IReadOnlyCollection<SecurityKey> initialKeys,
        IReadOnlyCollection<SecurityKey>? keysAfterRefresh = null)
    {
        _initialKeys = initialKeys;
        _keysAfterRefresh = keysAfterRefresh ?? initialKeys;
    }

    public int ForcedRefreshCount { get; private set; }

    public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh)
        {
            return Task.FromResult(_initialKeys);
        }

        ForcedRefreshCount++;
        return Task.FromResult(_keysAfterRefresh);
    }
}
