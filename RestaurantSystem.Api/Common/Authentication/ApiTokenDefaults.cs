namespace RestaurantSystem.Api.Common.Authentication;

/// <summary>
/// Constants shared by the API-token authentication scheme and everything that reasons about it
/// (docs/plans/API-TOKENS-PLAN.md §3).
/// </summary>
public static class ApiTokenDefaults
{
    /// <summary>Name of the authentication scheme that validates <c>sk_</c> bearer tokens.</summary>
    public const string AuthenticationScheme = "ApiToken";

    /// <summary>
    /// The policy scheme that picks between <see cref="AuthenticationScheme"/> and JWT by looking
    /// at the bearer value. It is the app's default scheme, so no endpoint needs new metadata.
    /// </summary>
    public const string SelectorScheme = "BearerSelector";

    /// <summary>
    /// Plaintext prefix. Load-bearing: it is how the selector tells a machine token from a JWT,
    /// and it is a shape published secret-scanners already recognise.
    /// </summary>
    public const string TokenPrefix = "sk_live_";

    /// <summary>Claim type carrying ONE granted scope. A token principal has one per scope.</summary>
    public const string ScopeClaimType = "api_scope";

    /// <summary>Claim type marking how the caller authenticated; <see cref="ApiTokenAuthMethod"/> for tokens.</summary>
    public const string AuthMethodClaimType = "auth_method";

    /// <summary>Value of <see cref="AuthMethodClaimType"/> for a machine token.</summary>
    public const string ApiTokenAuthMethod = "api_token";

    /// <summary>How stale <c>LastUsedAt</c> may get before a request pays for an UPDATE.</summary>
    public static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(1);

    /// <summary>Whether a raw bearer value looks like one of our tokens at all.</summary>
    public static bool LooksLikeApiToken(string? bearerValue) =>
        bearerValue is not null && bearerValue.StartsWith("sk_", StringComparison.Ordinal);
}
