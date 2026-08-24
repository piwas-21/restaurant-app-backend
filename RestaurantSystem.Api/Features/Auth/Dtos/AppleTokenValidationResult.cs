namespace RestaurantSystem.Api.Features.Auth.Dtos;

/// <summary>
/// Outcome of validating an Apple identity token. <see cref="Error"/> is a short technical
/// reason meant for the server log — never for the caller, who always gets a fixed message.
/// </summary>
public sealed record AppleTokenValidationResult
{
    public bool IsValid { get; private init; }

    /// <summary>
    /// True when the refusal is OURS, not the token's: Apple sign-in is not configured, or
    /// Apple's key endpoint could not be reached. Both fail closed, and both are a temporary
    /// server-side condition the caller may retry — unlike a rejected token.
    /// </summary>
    public bool IsUnavailable { get; private init; }

    public string? Error { get; private init; }

    public AppleIdentity? Identity { get; private init; }

    public static AppleTokenValidationResult Valid(AppleIdentity identity) =>
        new() { IsValid = true, Identity = identity };

    public static AppleTokenValidationResult Invalid(string error) =>
        new() { IsValid = false, Error = error };

    public static AppleTokenValidationResult Unavailable(string error) =>
        new() { IsValid = false, IsUnavailable = true, Error = error };
}
