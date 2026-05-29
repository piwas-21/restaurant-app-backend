using RestaurantSystem.Api.Features.Orders.Models;
using System.Collections.Concurrent;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Default <see cref="ISseClientManager"/>. Faithful extraction of the client registry,
/// the stale-client cleanup timer, and <c>GetClientStatistics</c> from OrderEventService;
/// the per-IP cap (10), stale timeout (3 min), cleanup cadence (30s), logging strings, and
/// statistics shape are unchanged. Owns the timer and client lifetimes (IDisposable).
/// </summary>
public class SseClientManager : ISseClientManager, IDisposable
{
    private readonly ConcurrentDictionary<string, SseClient> _clients = new();
    private readonly ISseActivityLog _activityLog;
    private readonly ISseEventReplayService _replay;
    private readonly ILogger<SseClientManager> _logger;

    private const int MaxConnectionsPerIp = 10;
    private readonly Timer _cleanupTimer;

    // Consider a client stale if no activity for 3 minutes (should receive heartbeats every 10 seconds)
    private static readonly TimeSpan StaleClientTimeout = TimeSpan.FromMinutes(3);

    public SseClientManager(
        ISseActivityLog activityLog,
        ISseEventReplayService replay,
        ILogger<SseClientManager> logger)
    {
        _activityLog = activityLog;
        _replay = replay;
        _logger = logger;

        // Run cleanup every 30 seconds for faster stale client detection
        _cleanupTimer = new Timer(CleanupStaleClients, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public IReadOnlyCollection<SseClient> GetClients() => _clients.Values.ToArray();

    private void CleanupStaleClients(object? state)
    {
        var now = DateTime.UtcNow;
        var staleClients = _clients.Values
            .Where(c => now - c.LastActivityAt > StaleClientTimeout)
            .ToList();

        if (staleClients.Any())
        {
            _logger.LogWarning("Found {Count} stale clients (no activity for {Minutes} minutes), removing...",
                staleClients.Count, StaleClientTimeout.TotalMinutes);

            foreach (var client in staleClients)
            {
                var msg = $"Removing stale client {client.ClientId} ({client.ClientType}) - no activity since {client.LastActivityAt}";
                _logger.LogWarning(msg);
                _activityLog.Add("Warning", msg, null, client.ClientId);
                RemoveClient(client.ClientId);
            }
        }
    }

    // Returns false if the IP has reached the per-IP connection limit.
    public bool TryAddClient(string clientId, SseClient client)
    {
        var connectionsFromIp = _clients.Values.Count(c => c.IpAddress == client.IpAddress);
        if (connectionsFromIp >= MaxConnectionsPerIp)
        {
            _logger.LogWarning("SSE connection limit reached for IP {IP} ({Count}/{Max})",
                client.IpAddress, connectionsFromIp, MaxConnectionsPerIp);
            return false;
        }

        _clients.TryAdd(clientId, client);
        var clientsByType = _clients.Values.GroupBy(c => c.ClientType).ToDictionary(g => g.Key, g => g.Count());
        var message = $"SSE client {clientId} ({client.ClientType}) connected from {client.IpAddress} ({client.Country ?? "Unknown"}). Total clients: {_clients.Count} (Kitchen: {clientsByType.GetValueOrDefault(ClientType.Kitchen, 0)}, Service: {clientsByType.GetValueOrDefault(ClientType.Service, 0)}, Manager: {clientsByType.GetValueOrDefault(ClientType.Manager, 0)}, Stock: {clientsByType.GetValueOrDefault(ClientType.Stock, 0)})";
        _logger.LogInformation(message);
        _activityLog.Add("Info", message, null, clientId);
        return true;
    }

    public void AddClient(string clientId, SseClient client) => TryAddClient(clientId, client);

    public void RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var removedClient))
        {
            // Dispose the semaphore to prevent memory leak
            removedClient.WriteLock.Dispose();

            var clientsByType = _clients.Values.GroupBy(c => c.ClientType).ToDictionary(g => g.Key, g => g.Count());
            var message = $"SSE client {clientId} ({removedClient.ClientType}) disconnected. Total clients: {_clients.Count} (Kitchen: {clientsByType.GetValueOrDefault(ClientType.Kitchen, 0)}, Service: {clientsByType.GetValueOrDefault(ClientType.Service, 0)}, Manager: {clientsByType.GetValueOrDefault(ClientType.Manager, 0)}, Stock: {clientsByType.GetValueOrDefault(ClientType.Stock, 0)})";

            _logger.LogInformation(message);
            _activityLog.Add("Info", message, null, clientId);

            // Dispose SemaphoreSlim to prevent memory leak
            removedClient.Dispose();
        }
    }

    public object GetClientStatistics()
    {
        var clientsByType = _clients.Values.GroupBy(c => c.ClientType)
            .ToDictionary(g => g.Key.ToString(), g => g.Select(c => new
            {
                clientId = c.ClientId,
                ipAddress = c.IpAddress,
                country = c.Country ?? "Unknown",
                connectedAt = c.ConnectedAt,
                connectedDuration = DateTime.UtcNow - c.ConnectedAt,
                lastActivityAt = c.LastActivityAt,
                timeSinceLastActivity = DateTime.UtcNow - c.LastActivityAt,
                successfulSends = c.SuccessfulSends,
                failedSends = c.FailedSends,
                lastEventSentAt = c.LastEventSentAt,
                errors = c.ErrorSnapshot().Select(e => new
                {
                    timestamp = e.Timestamp,
                    errorType = e.ErrorType,
                    message = e.Message,
                    eventType = e.EventType
                }).ToList(),
                hasErrors = c.ErrorSnapshot().Any(),
                errorCount = c.ErrorSnapshot().Count
            }).ToList());

        var allErrors = _clients.Values
            .SelectMany(c => c.ErrorSnapshot().Select(e => new
            {
                clientId = c.ClientId,
                clientType = c.ClientType.ToString(),
                ipAddress = c.IpAddress,
                country = c.Country ?? "Unknown",
                timestamp = e.Timestamp,
                errorType = e.ErrorType,
                message = e.Message,
                eventType = e.EventType
            }))
            .OrderByDescending(e => e.timestamp)
            .ToList();

        var recentLogs = _activityLog.Snapshot().OrderByDescending(l => l.Timestamp).Take(50).Select(l => new
        {
            timestamp = l.Timestamp,
            level = l.Level,
            message = l.Message,
            eventType = l.EventType,
            clientId = l.ClientId
        }).ToList();

        return new
        {
            totalClients = _clients.Count,
            kitchenClients = _clients.Values.Count(c => c.ClientType == ClientType.Kitchen),
            serviceClients = _clients.Values.Count(c => c.ClientType == ClientType.Service),
            managerClients = _clients.Values.Count(c => c.ClientType == ClientType.Manager),
            stockClients = _clients.Values.Count(c => c.ClientType == ClientType.Stock),
            clientsWithErrors = _clients.Values.Count(c => c.ErrorSnapshot().Any()),
            totalErrors = _clients.Values.Sum(c => c.ErrorSnapshot().Count),
            totalSuccessfulSends = _clients.Values.Sum(c => c.SuccessfulSends),
            totalFailedSends = _clients.Values.Sum(c => c.FailedSends),
            clientDetails = clientsByType,
            recentErrors = allErrors.Take(20).ToList(), // Last 20 errors across all clients
            recentLogs, // Last 50 log entries
            replayBuffer = _replay.GetBufferStatistics(), // Replay buffer statistics
            timestamp = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        // _cleanupTimer is a non-nullable, ctor-initialized field — no null-conditional needed.
        _cleanupTimer.Dispose();
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }
}
