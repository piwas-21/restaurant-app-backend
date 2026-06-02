using RestaurantSystem.Api.Features.Orders.Models;
using System.Collections.Concurrent;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Default <see cref="ISseEventReplayService"/>. Faithful extraction of the replay buffer,
/// <c>StoreEventForReplay</c>, <c>ReplayRecentEventsAsync</c>, and the replay-buffer
/// statistics block from OrderEventService; thresholds and behaviour are unchanged.
/// </summary>
public class SseEventReplayService : ISseEventReplayService
{
    private readonly ISseClientWriter _clientWriter;
    private readonly ISseActivityLog _activityLog;
    private readonly ILogger<SseEventReplayService> _logger;

    // Event replay buffer - stores recent events to replay to reconnecting clients
    private readonly ConcurrentQueue<ReplayableEvent> _eventReplayBuffer = new();
    private const int MaxReplayBufferSize = 500;  // Maximum events to buffer (increased from 50)
    private const int ReplayBufferTimeoutSeconds = 900;  // 15 minutes (increased from 60s) - events older than this are discarded

    public SseEventReplayService(
        ISseClientWriter clientWriter,
        ISseActivityLog activityLog,
        ILogger<SseEventReplayService> logger)
    {
        _clientWriter = clientWriter;
        _activityLog = activityLog;
        _logger = logger;
    }

    public void StoreEventForReplay(byte[] eventBytes, string eventType, ClientType targetClientType)
    {
        _eventReplayBuffer.Enqueue(new ReplayableEvent
        {
            EventBytes = eventBytes,
            EventType = eventType,
            TargetClientType = targetClientType,
            Timestamp = DateTime.UtcNow
        });

        // Remove old events from buffer (by count and time)
        while (_eventReplayBuffer.Count > MaxReplayBufferSize)
        {
            _eventReplayBuffer.TryDequeue(out _);
        }

        // Also remove events older than timeout
        var cutoff = DateTime.UtcNow.AddSeconds(-ReplayBufferTimeoutSeconds);
        while (_eventReplayBuffer.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            _eventReplayBuffer.TryDequeue(out _);
        }
    }

    public async Task ReplayRecentEventsAsync(SseClient client)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-ReplayBufferTimeoutSeconds);
        var eventsToReplay = _eventReplayBuffer
            .Where(e => e.Timestamp >= cutoff &&
                       (e.TargetClientType == ClientType.All ||
                        e.TargetClientType == client.ClientType ||
                        client.ClientType == ClientType.Manager))
            .ToList();

        var totalBufferSize = _eventReplayBuffer.Count;
        var validEvents = _eventReplayBuffer.Count(e => e.Timestamp >= cutoff);
        var expiredEvents = totalBufferSize - validEvents;

        if (eventsToReplay.Count == 0)
        {
            var msg = $"No recent events to replay for client {client.ClientId}. Buffer stats: {totalBufferSize} total, {validEvents} valid, {expiredEvents} expired";
            _logger.LogInformation(msg);
            _activityLog.Add("Info", msg, null, client.ClientId);
            return;
        }

        var eventSummary = string.Join(", ", eventsToReplay.GroupBy(e => e.EventType)
            .Select(g => $"{g.Key}: {g.Count()}"));

        var oldestEventAge = (DateTime.UtcNow - eventsToReplay.Min(e => e.Timestamp)).TotalSeconds;

        var replayMsg = $"🔄 Replaying {eventsToReplay.Count} recent event(s) to client {client.ClientId} ({client.ClientType}). Events: [{eventSummary}]. Oldest event: {oldestEventAge:F1}s ago. Buffer: {totalBufferSize} total, {validEvents} valid";
        _logger.LogInformation(replayMsg);
        _activityLog.Add("Info", replayMsg, null, client.ClientId);

        int successCount = 0;
        int failureCount = 0;

        // SendAsync catches all exceptions internally and signals DisconnectCts on failure,
        // so a try-catch here is dead code. Check the cancellation token instead to stop
        // replaying immediately when the client disconnects mid-replay.
        foreach (var replayEvent in eventsToReplay)
        {
            if (client.DisconnectCts.IsCancellationRequested)
            {
                failureCount++;
                break;
            }

            await _clientWriter.SendAsync(client, replayEvent.EventBytes, replayEvent.EventType);

            if (client.DisconnectCts.IsCancellationRequested)
            {
                failureCount++;
                break;
            }

            successCount++;
            _logger.LogDebug("Replayed event {EventType} to client {ClientId}",
                replayEvent.EventType, client.ClientId);
        }

        var completionMsg = $"Replay completed for client {client.ClientId}: {successCount} succeeded, {failureCount} failed";
        _logger.LogInformation(completionMsg);
        _activityLog.Add("Info", completionMsg, null, client.ClientId);
    }

    public object GetBufferStatistics()
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-ReplayBufferTimeoutSeconds);
        var validEvents = _eventReplayBuffer.Where(e => e.Timestamp >= cutoff).ToList();
        var expiredEvents = _eventReplayBuffer.Count - validEvents.Count;

        return new
        {
            totalEventsInBuffer = _eventReplayBuffer.Count,
            validEvents = validEvents.Count,
            expiredEvents,
            maxBufferSize = MaxReplayBufferSize,
            bufferTimeoutSeconds = ReplayBufferTimeoutSeconds,
            oldestEventAge = _eventReplayBuffer.Any()
                ? (now - _eventReplayBuffer.Min(e => e.Timestamp)).TotalSeconds
                : 0,
            newestEventAge = _eventReplayBuffer.Any()
                ? (now - _eventReplayBuffer.Max(e => e.Timestamp)).TotalSeconds
                : 0,
            eventsByType = validEvents.GroupBy(e => e.EventType)
                .Select(g => new { eventType = g.Key, count = g.Count() })
                .ToList(),
            eventsByClientType = validEvents.GroupBy(e => e.TargetClientType)
                .Select(g => new { clientType = g.Key.ToString(), count = g.Count() })
                .ToList()
        };
    }
}
