using RestaurantSystem.Api.Features.Auth.Dtos;

namespace RestaurantSystem.Api.Features.Auth.Interfaces;

/// <summary>
/// Verifies an Apple identity token: RS256 signature against Apple's JWKS, issuer, audience
/// and lifetime. Refuses everything when Apple is not configured (BACKEND-NOTES §4.1).
/// </summary>
public interface IAppleIdentityTokenVerifier
{
    Task<AppleTokenValidationResult> ValidateAsync(string idToken, CancellationToken cancellationToken);
}
