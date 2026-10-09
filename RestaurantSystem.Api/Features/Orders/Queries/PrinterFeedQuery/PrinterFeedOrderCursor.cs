using System.Globalization;
using System.Text;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

/// <summary>
/// Order pages use descending order date and ascending id. New tokens also freeze the required
/// queued-route recovery fence so every page in one drain observes the same candidate window.
/// </summary>
internal static class PrinterFeedOrderCursor
{
    private const string Version2 = "v2";
    private const char Separator = '|';
    private static readonly TimeSpan FutureClockSkew = TimeSpan.FromMinutes(1);

    public static string Encode(OrderDto order, DateTime recoveryCutoffUtc, DateTime issuedAtUtc)
    {
        var payload = string.Join(Separator,
            Version2,
            FormatUtc(order.OrderDate),
            order.Id.ToString("D", CultureInfo.InvariantCulture),
            FormatUtc(recoveryCutoffUtc),
            FormatUtc(issuedAtUtc));
        return EncodeBase64Url(payload);
    }

    public static OrderFeedCursor Decode(string value)
    {
        try
        {
            var payload = DecodeBase64Url(value);
            var fields = payload.Split(Separator);
            if (fields.Length == 2)
            {
                var (date, id) = PrinterFeedUpdateCursor.Decode(value);
                return new OrderFeedCursor(date, id, null, null);
            }

            if (fields.Length != 5 || fields[0] != Version2
                || !TryParseUtc(fields[1], out var orderDate)
                || !Guid.TryParseExact(fields[2], "D", out var orderId)
                || !TryParseUtc(fields[3], out var recoveryCutoffUtc)
                || !TryParseUtc(fields[4], out var issuedAtUtc))
            {
                throw new FormatException();
            }

            return new OrderFeedCursor(orderDate, orderId, recoveryCutoffUtc, issuedAtUtc);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or DecoderFallbackException)
        {
            throw new BadRequestException("The order cursor is invalid.");
        }
    }

    public static RecoveryFence ResolveRecoveryFence(
        string? cursor,
        DateTime nowUtc,
        OrderRoutingSettings settings)
    {
        var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var freshFence = new RecoveryFence(
            now.AddHours(-settings.RequiredQueuedRouteRecoveryHours), now);
        if (string.IsNullOrWhiteSpace(cursor))
            return freshFence;

        var decoded = Decode(cursor);
        if (!decoded.RecoveryCutoffUtc.HasValue || !decoded.IssuedAtUtc.HasValue)
        {
            // Older printer clients can continue an existing date/id cursor. Their continuation
            // starts a fresh recovery window because the legacy token did not freeze one.
            return freshFence;
        }

        var cutoff = decoded.RecoveryCutoffUtc.Value;
        var issuedAt = decoded.IssuedAtUtc.Value;
        var latestIssuedAt = now.Add(FutureClockSkew);
        var earliestIssuedAt = now.AddMinutes(-settings.RequiredQueuedRouteRecoveryCursorLifetimeMinutes);
        var expectedCutoff = issuedAt.AddHours(-settings.RequiredQueuedRouteRecoveryHours);
        if (issuedAt > latestIssuedAt || issuedAt < earliestIssuedAt
            || cutoff != expectedCutoff || cutoff > now.Add(FutureClockSkew))
        {
            throw new BadRequestException("The order cursor is expired or invalid.");
        }

        return new RecoveryFence(cutoff, issuedAt);
    }

    public static (DateTime OrderDate, Guid OrderId) DecodeOrderPosition(string value)
    {
        var decoded = Decode(value);
        return (decoded.OrderDate, decoded.OrderId);
    }

    private static string FormatUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static bool TryParseUtc(string value, out DateTime utcValue)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsed))
        {
            utcValue = parsed.UtcDateTime;
            return true;
        }

        utcValue = default;
        return false;
    }

    private static string EncodeBase64Url(string payload) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string DecodeBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new FormatException();
        }

        var padded = value.Replace('-', '+').Replace('_', '/')
            .PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    internal readonly record struct OrderFeedCursor(
        DateTime OrderDate,
        Guid OrderId,
        DateTime? RecoveryCutoffUtc,
        DateTime? IssuedAtUtc);

    internal readonly record struct RecoveryFence(DateTime CutoffUtc, DateTime IssuedAtUtc);
}
