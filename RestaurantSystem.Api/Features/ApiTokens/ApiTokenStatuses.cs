using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.ApiTokens;

/// <summary>
/// The three states an admin needs to tell apart in the token list, derived server-side so the
/// UI cannot invent a fourth or disagree with the authentication handler.
/// </summary>
public static class ApiTokenStatuses
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Revoked = "revoked";

    /// <summary>
    /// Revoked wins over expired: an admin who revoked a token wants to see that they did,
    /// even after the clock would have retired it anyway.
    /// </summary>
    public static string Of(ApiToken token, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.RevokedAt is not null) return Revoked;
        return token.ExpiresAt <= utcNow ? Expired : Active;
    }
}
