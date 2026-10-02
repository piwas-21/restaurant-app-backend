using System.Globalization;

namespace RestaurantSystem.Channels.Api;

public static class ProviderOrderTimestamp
{
    public static bool TryRead(string value, DateTimeOffset now, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var hasTimezone = value.EndsWith('Z')
            || value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':';
        return hasTimezone && DateTimeOffset.TryParseExact(value,
            ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp)
            && timestamp != default && timestamp <= now;
    }
}
