using RestaurantSystem.Api.Features.Orders.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Writes a single pre-serialised SSE event frame to one client, with a per-client write
/// lock, a send timeout, success/failure accounting, and disconnect signalling on failure.
/// Extracted from OrderEventService.SendToClient (Sprint 3 SSE decomposition). Singleton.
/// </summary>
public interface ISseClientWriter
{
    /// <summary>
    /// Sends <paramref name="eventBytes"/> to <paramref name="client"/>. No-op if the client
    /// is already flagged for disconnection. On timeout/error it records the error, increments
    /// the failure counter, and cancels the client's <c>DisconnectCts</c> (the heartbeat loop
    /// then cleans the client up). Does not throw for per-client send failures.
    /// </summary>
    Task SendAsync(SseClient client, byte[] eventBytes, string? eventType = null);
}
