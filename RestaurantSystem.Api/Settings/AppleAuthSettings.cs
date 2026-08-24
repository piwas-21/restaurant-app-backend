namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Sign in with Apple (BACKEND-NOTES §4.1). Fail-closed: with no client id configured the API
/// refuses every apple-login instead of trusting an unverified token.
/// </summary>
public class AppleAuthSettings
{
    public const string SectionName = "Authentication:Apple";

    /// <summary>Accepted <c>aud</c> values — one per client (iOS bundle id, web service id).</summary>
    public IList<string> ClientIds { get; set; } = new List<string>();

    /// <summary>Legacy single-value form of <see cref="ClientIds"/>, merged into it.</summary>
    public string? ClientId { get; set; }

    /// <summary>Apple's fixed <c>iss</c>. A protocol constant, overridable only for tests.</summary>
    public string Issuer { get; set; } = "https://appleid.apple.com";

    /// <summary>Apple's fixed JWKS endpoint. Protocol constant, as <see cref="Issuer"/>.</summary>
    public string JwksUri { get; set; } = "https://appleid.apple.com/auth/keys";

    /// <summary>How long a fetched key set is reused before it is re-fetched.</summary>
    public int JwksCacheMinutes { get; set; } = 60;

    /// <summary>HTTP timeout for the JWKS fetch. A login must not hang on Apple.</summary>
    public int JwksTimeoutSeconds { get; set; } = 10;

    /// <summary>Tolerance for <c>exp</c>/<c>nbf</c>, covering small clock drift only.</summary>
    public int ClockSkewSeconds { get; set; } = 60;

    /// <summary>Every accepted audience, legacy key included, blanks and duplicates removed.</summary>
    public IReadOnlyList<string> AllClientIds() =>
        (ClientIds ?? new List<string>())
            .Append(ClientId ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
