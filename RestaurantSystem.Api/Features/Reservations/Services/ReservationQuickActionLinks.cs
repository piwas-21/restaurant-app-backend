using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <inheritdoc cref="IReservationQuickActionLinks"/>
/// <remarks>
/// <para><b>Why a signature and not a stored nonce.</b> The order links solve the same problem with a
/// random column (<c>Order.QuickActionToken</c>). That shape cannot express the two things #402
/// actually needs: an EXPIRY, and a binding to the decision the link was minted for. Signing the
/// reservation's CURRENT status gets the second one for free — after the booking is approved or
/// rejected the stored status no longer matches what the token was signed over, so both buttons in
/// that mail stop working with no extra bookkeeping and no column to migrate.</para>
/// <para><b>Where the key comes from.</b> <c>ReservationQuickActions:SigningKey</c> when set;
/// otherwise the already-required <c>JwtSettings:Secret</c>. Either way the material is run through
/// HKDF with a purpose label, so the value used here is NOT the JWT signing key and cannot be
/// swapped for it in either direction. That fallback is deliberate: making the fix depend on a new
/// environment variable would mean a box that deploys the code before the variable either crashes
/// at startup or, worse, signs with an empty key.</para>
/// <para><b>Token shape.</b> <c>{unixExpiry}.{base64url(HMAC-SHA256)}</c>. The expiry is in the
/// clear because the signature covers it — moving it forward changes the payload and invalidates
/// the token. Nothing else about the reservation is in the URL.</para>
/// </remarks>
public sealed class ReservationQuickActionLinks : IReservationQuickActionLinks
{
    /// <summary>
    /// HKDF purpose label. Change it and every link in every inbox stops working — which is the
    /// point of it being a constant and not a setting.
    /// </summary>
    private const string KeyPurpose = "rumi:reservation-quick-action:v1";

    /// <summary>Prefix of the signed payload. Bump when the payload gains or loses a field.</summary>
    private const string PayloadVersion = "v1";

    private const int KeyLengthBytes = 32;

    private readonly ReservationQuickActionSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReservationQuickActionLinks> _logger;
    private readonly byte[] _key;

    public ReservationQuickActionLinks(
        IOptions<ReservationQuickActionSettings> settings,
        IOptions<JwtSettings> jwtSettings,
        TimeProvider clock,
        ILogger<ReservationQuickActionLinks> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jwtSettings);

        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
        _key = DeriveKey(_settings.SigningKey, jwtSettings.Value.Secret);
    }

    /// <inheritdoc />
    public string Mint(Guid reservationId, ReservationQuickAction action, ReservationStatus status)
    {
        var expiresAt = _clock.GetUtcNow().AddDays(_settings.LinkLifetimeDays).ToUnixTimeSeconds();
        var signature = Sign(reservationId, action, status, expiresAt);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{expiresAt}.{Base64Url.EncodeToString(signature)}");
    }

    /// <inheritdoc />
    public QuickActionLinkVerdict Verify(
        Guid reservationId,
        ReservationQuickAction action,
        ReservationStatus currentStatus,
        DateTime reservationCreatedAtUtc,
        string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return VerifyLegacy(reservationId, action, currentStatus, reservationCreatedAtUtc);
        }

        var separator = token.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == token.Length - 1)
        {
            return Refuse(reservationId, action, "malformed token");
        }

        if (!long.TryParse(
                token.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt))
        {
            return Refuse(reservationId, action, "malformed token");
        }

        if (expiresAt <= _clock.GetUtcNow().ToUnixTimeSeconds())
        {
            return Refuse(reservationId, action, "expired link");
        }

        var supplied = token.AsSpan(separator + 1);
        if (!Base64Url.IsValid(supplied))
        {
            return Refuse(reservationId, action, "malformed token");
        }

        // FixedTimeEquals over the raw signature bytes, not the strings: a plain == would
        // short-circuit at the first differing character and leak, one request at a time, how much
        // of a forged signature was right. Length is not hidden, which is harmless — every
        // signature this class issues is exactly KeyLengthBytes long.
        var matched = CryptographicOperations.FixedTimeEquals(
            Sign(reservationId, action, currentStatus, expiresAt),
            Base64Url.DecodeFromChars(supplied));

        // A mismatch is EITHER a forgery or a link whose reservation has since been decided, and
        // this method cannot tell them apart — by design, since the status is part of what is
        // signed. Both must be refused, so both share one message.
        return matched
            ? QuickActionLinkVerdict.SignatureValid
            : Refuse(reservationId, action, "signature mismatch, or the booking has already been decided");
    }

    /// <summary>
    /// The migration path: an alert mail sent before this change carries no token at all. Rather
    /// than break every such link in the restaurant's inbox on release day, one is honoured while
    /// its OWN reservation is younger than the grace window.
    /// </summary>
    /// <remarks>
    /// Anchored on the reservation rather than on a release date so the window closes by itself,
    /// booking by booking, with no scheduled follow-up: a reservation created after the release
    /// always has a token, so it can never take this path in practice. Still restricted to a
    /// Pending booking, so a legacy link is no more replayable than a signed one.
    /// </remarks>
    private QuickActionLinkVerdict VerifyLegacy(
        Guid reservationId,
        ReservationQuickAction action,
        ReservationStatus currentStatus,
        DateTime reservationCreatedAtUtc)
    {
        if (_settings.LegacyLinkGraceDays <= 0)
        {
            return Refuse(reservationId, action, "no token, and the legacy grace window is closed");
        }

        if (currentStatus != ReservationStatus.Pending)
        {
            return Refuse(reservationId, action, "no token, and the booking has already been decided");
        }

        // Every instant this system stores is UTC; Unspecified comes back from a provider that
        // dropped the kind, and reading it as anything but UTC would move the deadline by hours.
        var createdAt = AsUtc(reservationCreatedAtUtc);

        // The window alone would also cover a booking made AFTER this fix shipped, whose mail
        // always carries a token — leaving #402 open for the length of the window. A box that sets
        // the cutoff to its release timestamp keeps every already-sent mail working and closes that
        // on day one.
        if (_settings.LegacyLinkCutoffUtc is { } cutoff && createdAt >= AsUtc(cutoff))
        {
            return Refuse(reservationId, action, "no token, and the booking postdates the legacy cutoff");
        }

        if (_clock.GetUtcNow().UtcDateTime >= createdAt.AddDays(_settings.LegacyLinkGraceDays))
        {
            return Refuse(reservationId, action, "no token, and the legacy grace window has passed for this booking");
        }

        _logger.LogWarning(
            "Accepted a LEGACY unsigned quick-{Action} link for reservation {ReservationId} (created {CreatedAt:O}). " +
            "It predates link signing and is inside the {GraceDays}-day grace window. " +
            "Set ReservationQuickActions:LegacyLinkGraceDays to 0 to refuse these.",
            action, reservationId, createdAt, _settings.LegacyLinkGraceDays);

        return QuickActionLinkVerdict.Legacy;
    }

    /// <summary>
    /// Logs why, at warning level, and answers <see cref="QuickActionLinkVerdict.Refused"/>.
    /// The reason stays server-side: the caller gets one page for every refusal, so the route
    /// cannot be used to find out whether a reservation id exists.
    /// </summary>
    private QuickActionLinkVerdict Refuse(Guid reservationId, ReservationQuickAction action, string reason)
    {
        // The supplied token is never logged. It is a credential guess, and a log that stores
        // guesses turns a near miss into a written-down secret.
        _logger.LogWarning(
            "Refused quick-{Action} link for reservation {ReservationId}: {Reason}",
            action, reservationId, reason);

        return QuickActionLinkVerdict.Refused;
    }

    private byte[] Sign(Guid reservationId, ReservationQuickAction action, ReservationStatus status, long expiresAt)
    {
        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{PayloadVersion}|{reservationId:N}|{Label(action)}|{(int)status}|{expiresAt}");

        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>
    /// The action as the payload spells it. Written out rather than taken from the enum name so
    /// renaming <see cref="ReservationQuickAction"/> cannot silently invalidate live links.
    /// </summary>
    private static string Label(ReservationQuickAction action) => action switch
    {
        ReservationQuickAction.Approve => "approve",
        ReservationQuickAction.Reject => "reject",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>
    /// Reads a stored instant as UTC. <see cref="DateTimeKind.Unspecified"/> is what a config
    /// string without a zone binds to, and treating it as machine-local would move the cutoff by
    /// hours on a box that is not on UTC.
    /// </summary>
    private static DateTime AsUtc(DateTime instant) => instant.Kind switch
    {
        DateTimeKind.Utc => instant,
        DateTimeKind.Local => instant.ToUniversalTime(),
        _ => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
    };

    private static byte[] DeriveKey(string configuredKey, string jwtSecret)
    {
        var material = string.IsNullOrWhiteSpace(configuredKey) ? jwtSecret : configuredKey;
        if (string.IsNullOrWhiteSpace(material))
        {
            throw new InvalidOperationException(
                "Reservation quick-action links cannot be signed: set ReservationQuickActions:SigningKey " +
                "or JwtSettings:Secret.");
        }

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(material),
            KeyLengthBytes,
            salt: null,
            info: Encoding.UTF8.GetBytes(KeyPurpose));
    }
}
