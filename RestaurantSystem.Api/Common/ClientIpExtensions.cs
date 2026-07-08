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
        // StringValues.ToString() joins repeated headers with ", ", so a single
        // split-on-',' + last element covers both one header with a list and
        // multiple X-Forwarded-For headers.
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var lastHop = forwarded.Split(',')[^1].Trim();
            if (lastHop.Length > 0)
            {
                return lastHop;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
