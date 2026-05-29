using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Orders.Services;

public class OrderEventService : IOrderEventService, IDisposable
{
    private readonly ConcurrentDictionary<string, SseClient> _clients = new();
    private readonly ILogger<OrderEventService> _logger;
    private readonly ISseActivityLog _activityLog;
    private readonly ISseEventReplayService _replay;
    private readonly ISseClientWriter _clientWriter;
    private const int MaxConnectionsPerIp = 10;
    private readonly Timer _cleanupTimer;

    // Consider a client stale if no activity for 3 minutes (should receive heartbeats every 10 seconds)
    private static readonly TimeSpan StaleClientTimeout = TimeSpan.FromMinutes(3);

    public OrderEventService(
        ILogger<OrderEventService> logger,
        ISseActivityLog activityLog,
        ISseEventReplayService replay,
        ISseClientWriter clientWriter)
    {
        _logger = logger;
        _activityLog = activityLog;
        _replay = replay;
        _clientWriter = clientWriter;

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
                _activityLog.Add("Warning", msg, null, client.ClientId);
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

    /// <summary>
    /// Replays recent events to a newly connected client (delegated to the replay service).
    /// </summary>
    public Task ReplayRecentEventsAsync(SseClient client) => _replay.ReplayRecentEventsAsync(client);

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

        _activityLog.Add("Info", msg1, "order-created");
        _activityLog.Add("Info", msg2, "order-created");
        _activityLog.Add("Info", msg3, "order-created");

        // Check if any clients are connected
        if (kitchenClients == 0 && serviceClients == 0)
        {
            var warnMsg = $"⚠️ WARNING: No Kitchen or Service clients connected for {order.Status} order {order.OrderNumber}! Event stored for replay when clients reconnect.";
            _logger.LogWarning(warnMsg);
            _activityLog.Add("Warning", warnMsg, "order-created");
        }

        // Notify kitchen staff of new orders
        await SendEventToClients(eventData, ClientType.Kitchen);

        // Also notify service staff (cashiers) of new orders
        await SendEventToClients(eventData, ClientType.Service);

        var msg4 = $"=== Order creation notification process completed for order {order.OrderNumber} (check broadcast results above) ===";
        _logger.LogInformation(msg4);
        _activityLog.Add("Info", msg4, "order-created");
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
        _activityLog.Add("Info", msg1, "order-status-changed");

        var targetClientTypes = string.Join(", ", clientTypes);
        var msg2 = $"Notifying {targetClientTypes} clients about status change";
        _logger.LogInformation(msg2);
        _activityLog.Add("Info", msg2, "order-status-changed");

        foreach (var clientType in clientTypes)
        {
            await SendEventToClients(eventData, clientType);
        }

        var msg3 = $"Status change notification completed for order {order.OrderNumber}";
        _logger.LogInformation(msg3);
        _activityLog.Add("Info", msg3, "order-status-changed");
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
        _activityLog.Add("Info", broadcastMsg, eventData.EventType);

        if (targetClients.Count == 0)
        {
            var warnMsg = $"No clients to broadcast event {eventData.EventType} for type {targetClientType}";
            _logger.LogWarning(warnMsg);
            _activityLog.Add("Warning", warnMsg, eventData.EventType);

            // Still store event for replay - clients that connect soon will receive it
            _replay.StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);
            return;
        }

        // Store event for replay to newly connecting clients
        _replay.StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);

        // Send to all clients in parallel, but track each individually
        int successCount = 0;
        int failureCount = 0;
        var sendTasks = targetClients.Select(async client =>
        {
            try
            {
                await _clientWriter.SendAsync(client, eventBytes, eventData.EventType);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                var errorMsg = $"Unhandled exception in SendToClient for {client.ClientId}";
                _logger.LogError(ex, errorMsg);
                _activityLog.Add("Error", $"{errorMsg}: {ex.Message}", eventData.EventType, client.ClientId);
            }
        }).ToArray();

        await Task.WhenAll(sendTasks);

        var completeMsg = $"Event {eventData.EventType} broadcast completed: {successCount} succeeded, {failureCount} failed out of {targetClients.Count} clients";
        _logger.LogInformation(completeMsg);
        _activityLog.Add("Info", completeMsg, eventData.EventType);
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
        _activityLog.Add("Info", broadcastMsg, eventData.EventType);

        if (targetClients.Count == 0)
        {
            var warnMsg = $"No clients to broadcast event {eventData.EventType} for type {targetClientType}";
            _logger.LogWarning(warnMsg);
            _activityLog.Add("Warning", warnMsg, eventData.EventType);

            // Still store event for replay - clients that connect soon will receive it
            _replay.StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);
            return;
        }

        // Store event for replay to newly connecting clients
        _replay.StoreEventForReplay(eventBytes, eventData.EventType, targetClientType);

        // Send to all clients in parallel, but track each individually
        int successCount = 0;
        int failureCount = 0;
        var sendTasks = targetClients.Select(async client =>
        {
            try
            {
                await _clientWriter.SendAsync(client, eventBytes, eventData.EventType);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                var errorMsg = $"Unhandled exception in SendToClient for {client.ClientId}";
                _logger.LogError(ex, errorMsg);
                _activityLog.Add("Error", $"{errorMsg}: {ex.Message}", eventData.EventType, client.ClientId);
            }
        }).ToArray();

        await Task.WhenAll(sendTasks);

        var completeMsg = $"Event {eventData.EventType} broadcast completed: {successCount} succeeded, {failureCount} failed out of {targetClients.Count} clients";
        _logger.LogInformation(completeMsg);
        _activityLog.Add("Info", completeMsg, eventData.EventType);
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
}
