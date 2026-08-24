using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Auth.Services;

/// <summary>
/// Verifies an Apple identity token before anything in it is believed (BACKEND-NOTES §4.1).
/// Until this existed the handler merely DECODED the token, so an unsigned JWT carrying any
/// <c>email</c> claim took over that account.
/// <para>
/// What is enforced: an RS256 signature made by one of Apple's published keys, a signed token
/// (so <c>alg: none</c> is refused), <c>iss</c>, <c>aud</c> against the configured client ids,
/// and <c>exp</c>/<c>nbf</c> with a small skew. Missing configuration is a refusal, never a skip.
/// </para>
/// </summary>
public sealed class AppleIdentityTokenVerifier : IAppleIdentityTokenVerifier
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private const string EmailClaim = "email";
    private const string EmailVerifiedClaim = "email_verified";
    private const string NonceClaim = "nonce";

    private readonly IAppleSigningKeyProvider _keyProvider;
    private readonly AppleAuthSettings _settings;
    private readonly ILogger<AppleIdentityTokenVerifier> _logger;

    public AppleIdentityTokenVerifier(
        IAppleSigningKeyProvider keyProvider,
        IOptions<AppleAuthSettings> settings,
        ILogger<AppleIdentityTokenVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _keyProvider = keyProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AppleTokenValidationResult> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return AppleTokenValidationResult.Invalid("empty identity token");
        }

        var audiences = _settings.AllClientIds();
        if (audiences.Count == 0 || string.IsNullOrWhiteSpace(_settings.Issuer))
        {
            // Fail CLOSED. The previous implementation skipped the audience check when the
            // config was absent, which is exactly how "easier testing" became a way in.
            _logger.LogError(
                "Apple sign-in is not configured: set {Section}:ClientIds and {Section}:Issuer. " +
                "Every apple-login is refused until then.",
                AppleAuthSettings.SectionName, AppleAuthSettings.SectionName);
            return AppleTokenValidationResult.Unavailable("apple sign-in is not configured");
        }

        var result = await ValidateAgainstAppleKeysAsync(idToken, audiences, forceRefresh: false, cancellationToken);

        // Apple rotates its keys, so a token can legitimately name a `kid` this process has
        // never seen. Re-fetch ONCE, and only for that failure: the provider itself refuses to
        // re-fetch more often than its floor allows.
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            result = await ValidateAgainstAppleKeysAsync(idToken, audiences, forceRefresh: true, cancellationToken);
        }

        if (!result.IsValid)
        {
            var reason = result.Exception?.GetType().Name ?? "unknown";
            _logger.LogWarning(result.Exception, "Rejected an Apple identity token: {Reason}", reason);
            return AppleTokenValidationResult.Invalid(reason);
        }

        return Describe(result.ClaimsIdentity);
    }

    private async Task<TokenValidationResult> ValidateAgainstAppleKeysAsync(
        string idToken, IReadOnlyList<string> audiences, bool forceRefresh, CancellationToken cancellationToken)
    {
        var keys = await _keyProvider.GetSigningKeysAsync(forceRefresh, cancellationToken);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            TryAllIssuerSigningKeys = true,
            // Apple signs with RS256 only. Pinning the algorithm is what stops a token from
            // choosing its own — `alg: none`, or an HMAC keyed with a public key.
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ClockSkew = TimeSpan.FromSeconds(Math.Max(0, _settings.ClockSkewSeconds)),
        };

        return await TokenHandler.ValidateTokenAsync(idToken, parameters);
    }

    private static AppleTokenValidationResult Describe(ClaimsIdentity? identity)
    {
        var subject = identity?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return AppleTokenValidationResult.Invalid("token carries no subject");
        }

        var email = identity?.FindFirst(EmailClaim)?.Value;

        return AppleTokenValidationResult.Valid(new AppleIdentity(
            subject,
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            // Apple sends this as the STRING "true" on some tokens and a real boolean on others.
            IsTrue(identity?.FindFirst(EmailVerifiedClaim)?.Value),
            identity?.FindFirst(NonceClaim)?.Value));
    }

    private static bool IsTrue(string? claimValue) =>
        bool.TryParse(claimValue, out var parsed)
            ? parsed
            : string.Equals(claimValue, "1", StringComparison.Ordinal);
}
