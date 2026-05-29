using RestaurantSystem.Api.Features.Orders.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Buffers recently broadcast SSE events and replays them to clients that connect shortly
/// after, so a brief disconnect doesn't lose events. Extracted from OrderEventService
/// (Sprint 3 SSE decomposition). Singleton.
/// </summary>
public interface ISseEventReplayService
{
    /// <summary>Stores a serialised event frame for later replay, evicting by count and age.</summary>
    void StoreEventForReplay(byte[] eventBytes, string eventType, ClientType targetClientType);

    /// <summary>Replays the still-valid buffered events targeted at the given client.</summary>
    Task ReplayRecentEventsAsync(SseClient client);

    /// <summary>Diagnostics snapshot of the replay buffer (counts, ages, breakdowns).</summary>
    object GetBufferStatistics();
}
