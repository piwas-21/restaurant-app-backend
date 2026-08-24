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

    /// <summary>
    /// How long a token-LESS link still works, measured from the reservation's own
    /// <c>CreatedAt</c> — the migration path for alert mails already in the inbox. Every use is
    /// logged at warning level. Set to 0 to close the window (see README §Reservation quick-action links).
    /// </summary>
    public int LegacyLinkGraceDays { get; set; } = 14;

    /// <summary>
    /// Optional, and the RECOMMENDED setting at release: no reservation created at or after this
    /// instant may ever take the legacy token-less path.
    /// <para>
    /// The window above is anchored on each booking, which is what lets it close by itself — but on
    /// its own it also covers bookings made AFTER this fix shipped, whose mail always carries a
    /// token. That is the whole of backend #402 left open for the length of the window. Setting
    /// this to the release timestamp closes it on day one while still honouring every mail already
    /// in the inbox. Unset = window only.
    /// </para>
    /// </summary>
    public DateTime? LegacyLinkCutoffUtc { get; set; }
}
