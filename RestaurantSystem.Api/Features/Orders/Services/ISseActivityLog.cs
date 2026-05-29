using RestaurantSystem.Api.Features.Orders.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Bounded in-memory ring buffer of recent SSE activity log entries, surfaced by the
/// diagnostics endpoint. Extracted from OrderEventService (Sprint 3 SSE decomposition).
/// Singleton; thread-safe.
/// </summary>
public interface ISseActivityLog
{
    /// <summary>Appends an entry, evicting the oldest once the cap is reached.</summary>
    void Add(string level, string message, string? eventType = null, string? clientId = null);

    /// <summary>Returns a point-in-time snapshot of the buffered entries.</summary>
    IReadOnlyCollection<LogEntry> Snapshot();
}
