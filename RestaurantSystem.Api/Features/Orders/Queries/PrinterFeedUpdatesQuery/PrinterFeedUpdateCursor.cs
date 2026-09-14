using System.Globalization;
using System.Text;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;

/// <summary>Opaque cursor for the additive update stream. It carries the last update's timestamp and
/// job id so equal timestamps and bounded pages cannot lose work.</summary>
internal static class PrinterFeedUpdateCursor
{
    private const char Separator = '|';

    public static string Encode(DateTime createdAt, Guid jobId)
    {
        var timestamp = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
        var payload = $"{timestamp}{Separator}{jobId:D}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static (DateTime CreatedAt, Guid JobId) Decode(string value)
    {
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/')
                .PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = payload.LastIndexOf(Separator);
            if (separator <= 0
                || !DateTime.TryParse(
                    payload[..separator],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var createdAt)
                || !Guid.TryParse(payload[(separator + 1)..], out var jobId))
            {
                throw new FormatException();
            }

            return (DateTime.SpecifyKind(createdAt, DateTimeKind.Utc), jobId);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or DecoderFallbackException)
        {
            throw new BadRequestException("The update cursor is invalid.");
        }
    }
}
