using RestaurantSystem.Api.Features.Orders.Dtos;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Orders.Services;

public class OrderEventService : IOrderEventService, IDisposable
{
    private readonly ConcurrentDictionary<string, SseClient> _clients = new();
    private readonly ILogger<OrderEventService> _logger;
    private readonly ConcurrentQueue<LogEntry> _recentLogs = new();
    private const int MaxLogEntries = 100;
    private const int MaxConnectionsPerIp = 10;
    private readonly Timer _cleanupTimer;

    // Consider a client stale if no activity for 3 minutes (should receive heartbeats every 10 seconds)
    private static readonly TimeSpan StaleClientTimeout = TimeSpan.FromMinutes(3);

    // Event replay buffer - stores recent events to replay to reconnecting clients
    private readonly ConcurrentQueue<ReplayableEvent> _eventReplayBuffer = new();
    private const int MaxReplayBufferSize = 500;  // Maximum events to buffer (increased from 50)
    private const int ReplayBufferTimeoutSeconds = 900;  // 15 minutes (increased from 60s) - events older than this are discarded

    public OrderEventService(ILogger<OrderEventService> logger)
    {
        _logger = logger;

        // Run cleanup every 30 seconds for faster stale client detection
        _cleanupTimer = new Timer(CleanupStaleClients, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

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
                AddLog("Warning", msg, null, client.ClientId);
                RemoveClient(client.ClientId);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }

    private void AddLog(string level, string message, string? eventType = null, string? clientId = null)
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

    /// <summary>
    /// Stores an event in the replay buffer for newly connecting clients
    /// </summary>
    private void StoreEventForReplay(byte[] eventBytes, string eventType, ClientType targetClientType)
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

    /// <summary>
    /// Replays recent events to a newly connected client
    /// </summary>
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
            AddLog("Info", msg, null, client.ClientId);
            return;
        }

        var eventSummary = string.Join(", ", eventsToReplay.GroupBy(e => e.EventType)
            .Select(g => $"{g.Key}: {g.Count()}"));

        var oldestEventAge = (DateTime.UtcNow - eventsToReplay.Min(e => e.Timestamp)).TotalSeconds;

        var replayMsg = $"🔄 Replaying {eventsToReplay.Count} recent event(s) to client {client.ClientId} ({client.ClientType}). Events: [{eventSummary}]. Oldest event: {oldestEventAge:F1}s ago. Buffer: {totalBufferSize} total, {validEvents} valid";
        _logger.LogInformation(replayMsg);
        AddLog("Info", replayMsg, null, client.ClientId);

        int successCount = 0;
        int failureCount = 0;

        foreach (var replayEvent in eventsToReplay)
        {
            try
            {
                await SendToClient(client, replayEvent.EventBytes, replayEvent.EventType);
                successCount++;
                _logger.LogDebug("Replayed event {EventType} to client {ClientId}",
                    replayEvent.EventType, client.ClientId);
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogWarning(ex, "Failed to replay event {EventType} to client {ClientId}",
                    replayEvent.EventType, client.ClientId);
                break; // Stop replaying if client has issues
            }
        }

        var completionMsg = $"Replay completed for client {client.ClientId}: {successCount} succeeded, {failureCount} failed";
        _logger.LogInformation(completionMsg);
        AddLog("Info", completionMsg, null, client.ClientId);
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
        AddLog("Info", message, null, clientId);
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
            AddLog("Info", message, null, clientId);

            // Dispose SemaphoreSlim to prevent memory leak
            removedClient.Dispose();
        }
    }

    public async Task NotifyStockUpdate(string stock)
    {
        var eventData = new StockEvent
        {
            EventType = "stock-updated",
            PreviousStatus = stock,
            Timestamp = DateTime.UtcNow
        };
        // Notify all staff about the updated order
        await SendEventToClients(eventData, ClientType.All);
    }

    public async Task NotifyOrderCreated(OrderDto order)
    {
        var eventData = new OrderEvent
        {
            EventType = "order-created",
            Order = order,
            Timestamp = DateTime.UtcNow
        };

        var kitchenClients = _clients.Values.Count(c => c.ClientType == ClientType.Kitchen);
        var serviceClients = _clients.Values.Count(c => c.ClientType == ClientType.Service);
        var managerClients = _clients.Values.Count(c => c.ClientType == ClientType.Manager);
        var allClients = _clients.Count;

        var msg1 = $"=== ORDER CREATED: {order.OrderNumber} (Status: {order.Status}, Type: {order.Type}) ===";
        var msg2 = $"Total connected clients: {allClients} (Kitchen: {kitchenClients}, Service: {serviceClients}, Manager: {managerClients})";
        var msg3 = $"Attempting to notify {kitchenClients} kitchen, {serviceClients} service, and {managerClients} manager client(s) about {order.Status} order";

        _logger.LogInformation(msg1);
        _logger.LogInformation(msg2);
        _logger.LogInformation(msg3);

        AddLog("Info", msg1, "order-created");
        AddLog("Info", msg2, "order-created");
        AddLog("Info", msg3, "order-created");

        // Check if any clients are connected
        if (kitchenClients == 0 && serviceClients == 0)
        {
            var warnMsg = $"⚠️ WARNING: No Kitchen or Service clients connected for {order.Status} order {order.OrderNumber}! Event stored for replay when clients reconnect.";
            _logger.LogWarning(warnMsg);
            AddLog("Warning", warnMsg, "order-created");
        }

        // Notify kitchen staff of new orders
        await SendEventToClients(eventData, ClientType.Kitchen);

        // Also notify service staff (cashiers) of new orders
        await SendEventToClients(eventData, ClientType.Service);

        var msg4 = $"=== Order creation notification process completed for order {order.OrderNumber} (check broadcast results above) ===";
        _logger.LogInformation(msg4);
        AddLog("Info", msg4, "order-created");
    }

    public async Task NotifyOrderStatusChanged(OrderDto order, string previousStatus)
    {
        var eventData = new OrderEvent
        {
            EventType = "order-status-changed",
            Order = order,
            PreviousStatus = previousStatus,
            Timestamp = DateTime.UtcNow
        };

        // Determine which clients to notify based on status
        var clientTypes = GetClientTypesForStatus(order.Status);

        var msg1 = $"=== ORDER STATUS CHANGED: {order.OrderNumber} ({previousStatus} → {order.Status}) ===";
        _logger.LogInformation(msg1);
        AddLog("Info", msg1, "order-status-changed");

        var targetClientTypes = string.Join(", ", clientTypes);
        var msg2 = $"Notifying {targetClientTypes} clients about status change";
        _logger.LogInformation(msg2);
        AddLog("Info", msg2, "order-status-changed");

        foreach (var clientType in clientTypes)
        {
            await SendEventToClients(eventData, clientType);
        }

        var msg3 = $"Status change notification completed for order {order.OrderNumber}";
        _logger.LogInformation(msg3);
        AddLog("Info", msg3, "order-status-changed");
    }

    public async Task NotifyOrderReady(OrderDto order)
    {
        var eventData = new OrderEvent
        {
            EventType = "order-ready",
            Order = order,
            Timestamp = DateTime.UtcNow
        };

        // Notify service staff that order is ready
        await SendEventToClients(eventData, ClientType.Service);

        _logger.LogInformation("Notified service staff that order {OrderNumber} is ready", order.OrderNumber);
    }

    public async Task NotifyOrderCompleted(OrderDto order)
    {
        var eventData = new OrderEvent
        {
            EventType = "order-completed",
            Order = order,
            Timestamp = DateTime.UtcNow
        };

        // Notify all staff that order is completed
        await SendEventToClients(eventData, ClientType.All);

        _logger.LogInformation("Notified all staff that order {OrderNumber} is completed", order.OrderNumber);
    }

    public async Task NotifyFocusOrderUpdate(OrderDto order)
    {
        var eventData = new OrderEvent
        {
            EventType = "focus-order-update",
            Order = order,
            Timestamp = DateTime.UtcNow
        };

        // Notify all relevant staff about focus order updates
        await SendEventToClients(eventData, ClientType.All);

        _logger.LogInformation("Notified staff about focus order update for {OrderNumber}", order.OrderNumber);
    }

    private async Task SendEventToClients(StockEvent eventData, ClientType targetClientType)
    {
        var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var eventMessage = $"event: {eventData.EventType}\ndata: {json}\n\n";
        var eventBytes = Encoding.UTF8.GetBytes(eventMessage);

        // Create snapshot to avoid race conditions during iteration
        var targetClients = _clients.Values.Where(c =>
            targetClientType == ClientType.All || c.ClientType == targetClientType || c.ClientType == ClientType.Manager).ToList();

        var broadcastMsg = $"Broadcasting event {eventData.EventType} to {targetClients.Count} {targetClientType} client(s): [{string.Join(", ", targetClients.Select(c => c.ClientId))}]";
        _logger.LogInformation(broadcastMsg);
        AddLog("Info", broadcastMsg, eventData.EventType);

        if (targetClients.Count == 0)
        {
            var warnMsg = $"No clients to broadcast event {eventData.EventType} for type {targetClientType}";
            _logger.LogWarning(warnMsg);
            AddLog("Warning", warnMsg, eventData.EventType);

            // Still store event for replay - clients that connect soon will receive it
            StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);
            return;
        }

        // Store event for replay to newly connecting clients
        StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);

        // Send to all clients in parallel, but track each individually
        int successCount = 0;
        int failureCount = 0;
        var sendTasks = targetClients.Select(async client =>
        {
            try
            {
                await SendToClient(client, eventBytes, eventData.EventType);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                var errorMsg = $"Unhandled exception in SendToClient for {client.ClientId}";
                _logger.LogError(ex, errorMsg);
                AddLog("Error", $"{errorMsg}: {ex.Message}", eventData.EventType, client.ClientId);
            }
        }).ToArray();

        await Task.WhenAll(sendTasks);

        var completeMsg = $"Event {eventData.EventType} broadcast completed: {successCount} succeeded, {failureCount} failed out of {targetClients.Count} clients";
        _logger.LogInformation(completeMsg);
        AddLog("Info", completeMsg, eventData.EventType);
    }

    private async Task SendEventToClients(OrderEvent eventData, ClientType targetClientType)
    {
        var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var eventMessage = $"event: {eventData.EventType}\ndata: {json}\n\n";
        var eventBytes = Encoding.UTF8.GetBytes(eventMessage);

        var targetClients = _clients.Values.Where(c =>
            targetClientType == ClientType.All || c.ClientType == targetClientType || c.ClientType == ClientType.Manager).ToList();

        var broadcastMsg = $"Broadcasting event {eventData.EventType} to {targetClients.Count} {targetClientType} client(s): [{string.Join(", ", targetClients.Select(c => c.ClientId))}]";
        _logger.LogInformation(broadcastMsg);
        AddLog("Info", broadcastMsg, eventData.EventType);

        if (targetClients.Count == 0)
        {
            var warnMsg = $"No clients to broadcast event {eventData.EventType} for type {targetClientType}";
            _logger.LogWarning(warnMsg);
            AddLog("Warning", warnMsg, eventData.EventType);

            // Still store event for replay - clients that connect soon will receive it
            StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);
            return;
        }

        // Store event for replay to newly connecting clients
        StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);

        // Send to all clients in parallel, but track each individually
        int successCount = 0;
        int failureCount = 0;
        var sendTasks = targetClients.Select(async client =>
        {
            try
            {
                await SendToClient(client, eventBytes, eventData.EventType);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                var errorMsg = $"Unhandled exception in SendToClient for {client.ClientId}";
                _logger.LogError(ex, errorMsg);
                AddLog("Error", $"{errorMsg}: {ex.Message}", eventData.EventType, client.ClientId);
            }
        }).ToArray();

        await Task.WhenAll(sendTasks);

        var completeMsg = $"Event {eventData.EventType} broadcast completed: {successCount} succeeded, {failureCount} failed out of {targetClients.Count} clients";
        _logger.LogInformation(completeMsg);
        AddLog("Info", completeMsg, eventData.EventType);
    }

    private async Task SendToClient(SseClient client, byte[] eventBytes, string? eventType = null)
    {
        // Skip if client is already marked for disconnection
        if (client.DisconnectCts.IsCancellationRequested)
        {
            _logger.LogDebug("Skipping send to client {ClientId} - already marked for disconnection", client.ClientId);
            return;
        }

        try
        {
            var sendingMsg = $"Sending event to client {client.ClientId} ({client.ClientType}), {eventBytes.Length} bytes";
            _logger.LogInformation(sendingMsg);
            AddLog("Info", sendingMsg, eventType, client.ClientId);

            // Use timeout to prevent hanging on dead connections (5 seconds max per client)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Acquire lock to prevent concurrent writes (heartbeat vs event)
            await client.WriteLock.WaitAsync(cts.Token);
            try
            {
                await client.Response.Body.WriteAsync(eventBytes, cts.Token);
                await client.Response.Body.FlushAsync(cts.Token);
            }
            finally
            {
                client.WriteLock.Release();
            }

            // Track successful send
            client.SuccessfulSends++;
            client.LastEventSentAt = DateTime.UtcNow;
            client.LastActivityAt = DateTime.UtcNow; // Update activity timestamp to prevent stale cleanup

            var successMsg = $"✓ Event successfully sent to client {client.ClientId}";
            _logger.LogInformation(successMsg);
            AddLog("Info", successMsg, eventType, client.ClientId);
        }
        catch (OperationCanceledException)
        {
            var timeoutMsg = $"✗ Timeout sending event to client {client.ClientId} - signaling disconnect";
            _logger.LogWarning(timeoutMsg);
            AddLog("Warning", timeoutMsg, eventType, client.ClientId);

            // Track error (limit to last 10 errors per client to prevent unbounded growth)
            client.FailedSends++;
            client.Errors.Add(new ClientError
            {
                Timestamp = DateTime.UtcNow,
                ErrorType = "Timeout",
                Message = "Timeout sending event (5 seconds exceeded)",
                EventType = eventType
            });

            // Keep only last 10 errors per client
            if (client.Errors.Count > 10)
            {
                client.Errors.RemoveAt(0);
            }

            // Signal disconnect - this will cause the heartbeat loop to exit and clean up properly
            client.DisconnectCts.Cancel();
        }
        catch (Exception ex)
        {
            var errorMsg = $"✗ Failed to send event to client {client.ClientId} - signaling disconnect: {ex.Message}";
            _logger.LogError(ex, "✗ Failed to send event to client {ClientId} - signaling disconnect", client.ClientId);
            AddLog("Error", errorMsg, eventType, client.ClientId);

            // Track error (limit to last 10 errors per client to prevent unbounded growth)
            client.FailedSends++;
            client.Errors.Add(new ClientError
            {
                Timestamp = DateTime.UtcNow,
                ErrorType = ex.GetType().Name,
                Message = ex.Message,
                EventType = eventType
            });

            // Keep only last 10 errors per client
            if (client.Errors.Count > 10)
            {
                client.Errors.RemoveAt(0);
            }

            // Signal disconnect - this will cause the heartbeat loop to exit and clean up properly
            client.DisconnectCts.Cancel();
        }
    }

    private List<ClientType> GetClientTypesForStatus(string status)
    {
        // Service (cashiers) should always be notified of status changes
        // Kitchen should be notified for statuses they care about
        return status switch
        {
            "Pending" or "PendingApproval" => new List<ClientType> { ClientType.Kitchen, ClientType.Service },
            "Confirmed" or "Preparing" => new List<ClientType> { ClientType.Kitchen, ClientType.Service },
            "Ready" => new List<ClientType> { ClientType.Kitchen, ClientType.Service },
            "OutForDelivery" or "Completed" or "Cancelled" => new List<ClientType> { ClientType.All },
            _ => new List<ClientType> { ClientType.All }
        };
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
                errors = c.Errors.Select(e => new
                {
                    timestamp = e.Timestamp,
                    errorType = e.ErrorType,
                    message = e.Message,
                    eventType = e.EventType
                }).ToList(),
                hasErrors = c.Errors.Any(),
                errorCount = c.Errors.Count
            }).ToList());

        var allErrors = _clients.Values
            .SelectMany(c => c.Errors.Select(e => new
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

        var recentLogs = _recentLogs.OrderByDescending(l => l.Timestamp).Take(50).Select(l => new
        {
            timestamp = l.Timestamp,
            level = l.Level,
            message = l.Message,
            eventType = l.EventType,
            clientId = l.ClientId
        }).ToList();

        // Get replay buffer statistics
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-ReplayBufferTimeoutSeconds);
        var validEvents = _eventReplayBuffer.Where(e => e.Timestamp >= cutoff).ToList();
        var expiredEvents = _eventReplayBuffer.Count - validEvents.Count;

        var replayBufferStats = new
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

        return new
        {
            totalClients = _clients.Count,
            kitchenClients = _clients.Values.Count(c => c.ClientType == ClientType.Kitchen),
            serviceClients = _clients.Values.Count(c => c.ClientType == ClientType.Service),
            managerClients = _clients.Values.Count(c => c.ClientType == ClientType.Manager),
            stockClients = _clients.Values.Count(c => c.ClientType == ClientType.Stock),
            clientsWithErrors = _clients.Values.Count(c => c.Errors.Any()),
            totalErrors = _clients.Values.Sum(c => c.Errors.Count),
            totalSuccessfulSends = _clients.Values.Sum(c => c.SuccessfulSends),
            totalFailedSends = _clients.Values.Sum(c => c.FailedSends),
            clientDetails = clientsByType,
            recentErrors = allErrors.Take(20).ToList(), // Last 20 errors across all clients
            recentLogs, // Last 50 log entries
            replayBuffer = replayBufferStats, // Replay buffer statistics
            timestamp = DateTime.UtcNow
        };
    }

    public class SseClient : IDisposable
    {
        public string ClientId { get; set; } = null!;
        public HttpResponse Response { get; set; } = null!;
        public ClientType ClientType { get; set; }
        public DateTime ConnectedAt { get; set; }
        public string IpAddress { get; set; } = null!;
        public string? Country { get; set; }

        // Synchronization for concurrent writes (heartbeats vs events)
        public SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);

        // Cancellation token to signal when client should disconnect
        public CancellationTokenSource DisconnectCts { get; } = new CancellationTokenSource();

        // Error tracking
        public List<ClientError> Errors { get; } = new List<ClientError>();
        public int SuccessfulSends { get; set; } = 0;
        public int FailedSends { get; set; } = 0;
        public DateTime? LastEventSentAt { get; set; }
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

        private bool _disposed = false;

        public void Dispose()
        {
            if (!_disposed)
            {
                DisconnectCts?.Cancel();
                DisconnectCts?.Dispose();
                WriteLock?.Dispose();
                _disposed = true;
            }
        }
    }

    public class ClientError
    {
        public DateTime Timestamp { get; set; }
        public string ErrorType { get; set; } = null!;
        public string Message { get; set; } = null!;
        public string? EventType { get; set; }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = null!;
        public string Message { get; set; } = null!;
        public string? EventType { get; set; }
        public string? ClientId { get; set; }
    }

    public class OrderEvent
    {
        public string EventType { get; set; } = null!;
        public OrderDto Order { get; set; } = null!;
        public string? PreviousStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class StockEvent
    {
        public string EventType { get; set; } = null!;
        public string? PreviousStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum ClientType
    {
        Kitchen,
        Service,
        Manager,
        Stock,
        All
    }

    /// <summary>
    /// Represents an event stored for replay to newly connecting clients
    /// </summary>
    public class ReplayableEvent
    {
        public byte[] EventBytes { get; set; } = null!;
        public string EventType { get; set; } = null!;
        public ClientType TargetClientType { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
