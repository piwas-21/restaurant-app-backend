using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardCursorPolicy
{
    internal static string CreateFilter(ICurrentUserService caller) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f',
            "kitchen-board:v1",
            $"caller={caller.UserId?.ToString("N", CultureInfo.InvariantCulture) ?? string.Empty}",
            $"role={caller.Role?.ToString() ?? string.Empty}",
            $"apiToken={caller.IsApiToken}")))).ToLowerInvariant();

    internal static OperationalQueueCursorPayload Read(
        IOperationalQueueCursor cursor,
        string? value,
        string filterHash,
        string stream,
        int pageSize)
    {
        var payload = cursor.Read(value);
        if (!string.Equals(payload.FilterHash, BindStream(filterHash, stream), StringComparison.Ordinal)
            || payload.PageSize != pageSize
            || payload.Mode is not (OperationalQueueSyncModes.Snapshot
                or OperationalQueueSyncModes.Changes or OperationalQueueSyncModes.Watermark))
        {
            throw InvalidCursor();
        }

        return payload;
    }

    internal static string BindStream(string filterHash, string stream) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{filterHash}\u001f{stream}"))).ToLowerInvariant();

    internal static long ParsePosition(OperationalQueueCursorPayload payload)
    {
        if (!long.TryParse(payload.Position, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || sequence < 0)
        {
            throw InvalidCursor();
        }

        return sequence;
    }

    internal static string Protect(
        IOperationalQueueCursor cursor,
        string filterHash,
        string stream,
        OperationalQueueCursorRequest request) => cursor.Protect(request with
        {
            FilterHash = BindStream(filterHash, stream),
        });

    internal static BadRequestException InvalidCursor() => new(
        "The kitchen board synchronization cursor is invalid.",
        ErrorCodes.InvalidOperationalQueueCursor);
}
