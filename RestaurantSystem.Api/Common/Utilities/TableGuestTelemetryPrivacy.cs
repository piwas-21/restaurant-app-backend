using Sentry;

namespace RestaurantSystem.Api.Common.Utilities;

/// <summary>Visit and basket capabilities must never leave the API in error-report headers.</summary>
public static class TableGuestTelemetryPrivacy
{
    public static SentryEvent Filter(SentryEvent sentryEvent)
    {
        var headers = sentryEvent.Request.Headers;
        foreach (var key in headers.Keys.Where(IsCapabilityHeader).ToArray())
            headers.Remove(key);
        return sentryEvent;
    }

    private static bool IsCapabilityHeader(string key) =>
        string.Equals(key, "X-Table-Participant", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "X-Account-Payment-Receipt", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "X-Session-Id", StringComparison.OrdinalIgnoreCase);
}
