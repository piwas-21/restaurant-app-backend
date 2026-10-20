namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Signing and lifetime of the quick-approve / quick-reject links carried by the restaurant's
/// reservation alert mail (backend #402).
/// </summary>
public class ReservationQuickActionSettings
{
    public const string SectionName = "ReservationQuickActions";

    /// <summary>
    /// HMAC key material. LEAVE EMPTY on an existing box: the key is then derived from the
    /// already-required <c>JwtSettings:Secret</c> (see <c>ReservationQuickActionLinks</c>), which
    /// is why this fix needs no new environment variable to deploy. Set it to rotate the link
    /// signature independently of the JWT secret.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long a freshly minted link stays valid. The restaurant decides in the dashboard after that.</summary>
    public int LinkLifetimeDays { get; set; } = 7;

    /// <summary>Deprecated configuration compatibility only. Revisionless links are always refused.</summary>
    public int LegacyLinkGraceDays { get; set; } = 14;

    /// <summary>Deprecated configuration compatibility only; cannot authorize a revisionless link.</summary>
    public DateTime? LegacyLinkCutoffUtc { get; set; }
}
