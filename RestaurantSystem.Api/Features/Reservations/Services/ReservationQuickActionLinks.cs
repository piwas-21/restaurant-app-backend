using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>Purpose-separated, expiring authority over one exact database booking revision.</summary>
public sealed class ReservationQuickActionLinks : IReservationQuickActionLinks
{
    private const string KeyPurpose = "rumi:reservation-quick-action:v2";
    private const string PayloadVersion = "v2";
    private const int KeyLengthBytes = 32;
    private const int MaximumTokenLength = 128;
    private readonly ReservationQuickActionSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReservationQuickActionLinks> _logger;
    private readonly byte[] _key;

    public ReservationQuickActionLinks(IOptions<ReservationQuickActionSettings> settings,
        IOptions<JwtSettings> jwtSettings, TimeProvider clock, ILogger<ReservationQuickActionLinks> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jwtSettings);
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
        _key = DeriveKey(_settings.SigningKey, jwtSettings.Value.Secret);
    }
    public string Mint(Guid reservationId, ReservationQuickAction action, ReservationStatus status, Guid revision)
    {
        if (revision == Guid.Empty) throw new ArgumentException("An actual saved booking revision is required.", nameof(revision));
        var expiresAt = _clock.GetUtcNow().AddDays(_settings.LinkLifetimeDays).ToUnixTimeSeconds();
        return string.Create(CultureInfo.InvariantCulture,
            $"{PayloadVersion}.{expiresAt}.{Base64Url.EncodeToString(Sign(reservationId, action, status, revision, expiresAt))}");
    }
    public QuickActionLinkVerdict Verify(Guid reservationId, ReservationQuickAction action, ReservationStatus currentStatus,
        Guid currentRevision, string? token)
    {
        // Neither unsigned nor status-only v1 authority can distinguish a returned Pending status.
        // Legacy configuration remains bindable but cannot grant authority to a revisionless link.
        if (string.IsNullOrEmpty(token) || token.Length > MaximumTokenLength || currentRevision == Guid.Empty)
            return Refuse(reservationId, action, "missing or unsupported revision-bound proof");
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != PayloadVersion
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            || !Base64Url.IsValid(parts[2])) return Refuse(reservationId, action, "malformed or old-format token");
        if (expiresAt <= _clock.GetUtcNow().ToUnixTimeSeconds()) return Refuse(reservationId, action, "expired link");
        var supplied = Base64Url.DecodeFromChars(parts[2]);
        var matched = CryptographicOperations.FixedTimeEquals(Sign(reservationId, action, currentStatus, currentRevision, expiresAt), supplied);
        return matched ? QuickActionLinkVerdict.SignatureValid
            : Refuse(reservationId, action, "signature mismatch or the booking changed");
    }
    private QuickActionLinkVerdict Refuse(Guid reservationId, ReservationQuickAction action, string reason)
    {
        // A credential guess never enters logs or the public uniform refusal page.
        _logger.LogWarning("Refused quick-{Action} link for reservation {ReservationId}: {Reason}", action, reservationId, reason);
        return QuickActionLinkVerdict.Refused;
    }
    private byte[] Sign(Guid reservationId, ReservationQuickAction action, ReservationStatus status, Guid revision, long expiresAt)
    {
        var payload = string.Create(CultureInfo.InvariantCulture,
            $"{PayloadVersion}|{reservationId:N}|{Label(action)}|{(int)status}|{revision:N}|{expiresAt}");
        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
    }
    private static string Label(ReservationQuickAction action) => action switch
    {
        ReservationQuickAction.Approve => "approve",
        ReservationQuickAction.Reject => "reject",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    private static byte[] DeriveKey(string configuredKey, string jwtSecret)
    {
        var material = string.IsNullOrWhiteSpace(configuredKey) ? jwtSecret : configuredKey;
        if (string.IsNullOrWhiteSpace(material))
            throw new InvalidOperationException("Reservation quick-action links require ReservationQuickActions:SigningKey or JwtSettings:Secret.");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(material), KeyLengthBytes,
            salt: null, info: Encoding.UTF8.GetBytes(KeyPurpose));
    }
}
