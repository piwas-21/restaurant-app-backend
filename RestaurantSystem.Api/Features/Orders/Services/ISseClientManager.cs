using RestaurantSystem.Api.Features.Orders.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Owns the set of connected SSE clients: registration (with a per-IP cap), removal, the
/// background stale-client cleanup, and the diagnostics statistics. Extracted from
/// OrderEventService (Sprint 3 SSE decomposition, task 3.8). Singleton.
/// </summary>
public interface ISseClientManager
{
    /// <summary>Registers a client unless its IP has reached the connection cap. Returns false if rejected.</summary>
    bool TryAddClient(string clientId, SseClient client);

    /// <summary>Registers a client (ignores the rejection result; kept for API compatibility).</summary>
    void AddClient(string clientId, SseClient client);

    /// <summary>Removes a client and disposes its synchronization primitives.</summary>
    void RemoveClient(string clientId);

    /// <summary>A point-in-time snapshot of the currently connected clients (safe to enumerate).</summary>
    IReadOnlyCollection<SseClient> GetClients();

    /// <summary>Diagnostics: per-client/aggregate connection, send, error, log and replay-buffer stats.</summary>
    object GetClientStatistics();
}
