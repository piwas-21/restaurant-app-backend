using RestaurantSystem.Api.Features.Orders.Models;
using System.Collections.Concurrent;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Default <see cref="ISseActivityLog"/>. Faithful extraction of the <c>_recentLogs</c>
/// buffer and <c>AddLog</c> from OrderEventService; bound and behaviour unchanged.
/// </summary>
public class SseActivityLog : ISseActivityLog
{
    private const int MaxLogEntries = 100;
    private readonly ConcurrentQueue<LogEntry> _recentLogs = new();

    public void Add(string level, string message, string? eventType = null, string? clientId = null)
    {
        _recentLogs.Enqueue(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message,
            EventType = eventType,
            ClientId = clientId
        });

        // Keep only last MaxLogEntries
        while (_recentLogs.Count > MaxLogEntries)
        {
            _recentLogs.TryDequeue(out _);
        }
    }

    public IReadOnlyCollection<LogEntry> Snapshot() => _recentLogs.ToArray();
}
