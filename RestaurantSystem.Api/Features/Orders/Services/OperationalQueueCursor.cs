using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Modes carried by an operational queue synchronization cursor.</summary>
public static class OperationalQueueSyncModes
{
    public const string Snapshot = "Snapshot";
    public const string Changes = "Changes";
    public const string Watermark = "Watermark";
}

/// <summary>
/// Protected cursor payload. The client receives only the Data Protection result; all fields here
/// are authenticated server state, not client timestamps or offsets.
/// </summary>
public sealed record OperationalQueueCursorPayload(
    int Version,
    string Mode,
    string TenantKey,
    string FilterHash,
    long UpperSequence,
    long LowerSequence,
    string? Position,
    Guid? PositionId,
    int Page,
    int PageSize,
    int TotalCount,
    long IssuedAtTicks,
    long ExpiresAtTicks);

/// <summary>Issues and validates opaque operational queue synchronization cursors.</summary>
public sealed class OperationalQueueCursor : IOperationalQueueCursor
{
    private const int CurrentVersion = 1;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly OperationalQueueSyncOptions _options;

    public OperationalQueueCursor(
        IDataProtectionProvider protectionProvider,
        IOptions<OperationalQueueSyncOptions> options,
        TimeProvider timeProvider)
    {
        _protector = protectionProvider.CreateProtector(
            "RestaurantSystem.Orders.OperationalQueueSync.v1");
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public string Protect(OperationalQueueCursorRequest request)
    {
        var issuedAt = UtcNowTicks;
        var expiresAt = issuedAt + _options.CursorLifetime.Ticks;
        var payload = new OperationalQueueCursorPayload(
            CurrentVersion,
            request.Mode,
            TenantKey,
            request.FilterHash,
            request.UpperSequence,
            request.LowerSequence,
            request.Position,
            request.PositionId,
            request.Page,
            request.PageSize,
            request.TotalCount,
            issuedAt,
            expiresAt);

        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    public OperationalQueueCursorPayload Read(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > _options.MaxCursorLength)
        {
            throw InvalidCursor();
        }

        string json;
        try
        {
            json = _protector.Unprotect(value);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            throw InvalidCursor();
        }

        OperationalQueueCursorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OperationalQueueCursorPayload>(json);
        }
        catch (JsonException)
        {
            throw InvalidCursor();
        }

        if (payload is null || !IsStructurallyValid(payload))
        {
            throw InvalidCursor();
        }

        if (UtcNowTicks > payload.ExpiresAtTicks)
        {
            throw new BadRequestException(
                "The operational queue synchronization cursor has expired.",
                ErrorCodes.ExpiredOperationalQueueCursor);
        }

        return payload;
    }

    internal string TenantKey => string.IsNullOrWhiteSpace(_options.TenantKey)
        ? "legacy"
        : _options.TenantKey.Trim();

    private long UtcNowTicks => _timeProvider.GetUtcNow().UtcDateTime.Ticks;

    private bool IsStructurallyValid(OperationalQueueCursorPayload payload) =>
        payload.Version == CurrentVersion
        && payload.Mode is OperationalQueueSyncModes.Snapshot
            or OperationalQueueSyncModes.Changes
            or OperationalQueueSyncModes.Watermark
        && string.Equals(payload.TenantKey, TenantKey, StringComparison.Ordinal)
        && payload.FilterHash is { Length: 64 }
        && payload.FilterHash.All(Uri.IsHexDigit)
        && payload.UpperSequence >= 0
        && payload.LowerSequence >= 0
        && payload.Page >= 1
        && payload.PageSize >= 1
        && payload.PageSize <= _options.MaxPageSize
        && payload.TotalCount >= 0
        && payload.IssuedAtTicks > 0
        && payload.ExpiresAtTicks >= payload.IssuedAtTicks
        && payload.ExpiresAtTicks - payload.IssuedAtTicks <= _options.CursorLifetime.Ticks
        && (payload.Mode switch
        {
            OperationalQueueSyncModes.Snapshot => payload.Position is not null && payload.PositionId is not null,
            OperationalQueueSyncModes.Changes => payload.Position is not null && payload.PositionId is null,
            OperationalQueueSyncModes.Watermark => payload.Position is null && payload.PositionId is null,
            _ => false
        });

    private static BadRequestException InvalidCursor() => new(
        "The operational queue synchronization cursor is invalid.",
        ErrorCodes.InvalidOperationalQueueCursor);
}
