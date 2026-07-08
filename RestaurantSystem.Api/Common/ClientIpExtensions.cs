using Microsoft.AspNetCore.Http;

namespace RestaurantSystem.Api.Common;

public static class ClientIpExtensions
{
    /// <summary>
    /// The real client IP behind Caddy. Caddy (the sole edge proxy) APPENDS the
    /// connecting client's IP as the LAST X-Forwarded-For hop, so the rightmost
    /// token is trustworthy; the leftmost is client-supplied and spoofable. Reading
    /// the last hop makes an attacker's prepended values a no-op — the same fix as
    /// sofra #30. Falls back to the direct connection IP when the header is absent
    /// (local/dev, or a direct hit that bypasses the proxy).
    /// </summary>
    public static string GetClientIp(this HttpContext context)
    {
        // StringValues.ToString() joins repeated headers with ", ", so one split
        // covers both a single comma-list header and multiple X-Forwarded-For
        // headers. RemoveEmptyEntries|TrimEntries makes the last hop the last
        // *non-empty* token, so a trailing comma / stray whitespace can't collapse
        // it to "" and wrongly fall back to Caddy's proxy IP.
        var hops = context.Request.Headers["X-Forwarded-For"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hops.Length > 0)
        {
            return hops[^1];
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
