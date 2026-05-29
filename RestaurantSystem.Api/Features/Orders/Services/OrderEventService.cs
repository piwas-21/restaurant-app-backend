using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Facade over the SSE subsystem (Sprint 3 decomposition, task 3.11). Composes the
/// per-responsibility services — <see cref="ISseClientManager"/> (connections + stats),
/// <see cref="ISseBroadcastService"/> (fan-out), and <see cref="ISseEventReplayService"/>
/// (reconnect replay) — behind the existing <see cref="IOrderEventService"/> surface so
/// controllers and command handlers are unaffected. This class now only orchestrates the
/// per-notification logging and client routing; the mechanics live in the services above.
/// </summary>
public class OrderEventService : IOrderEventService
{
    private readonly ILogger<OrderEventService> _logger;
    private readonly ISseActivityLog _activityLog;
    private readonly ISseClientManager _clientManager;
    private readonly ISseBroadcastService _broadcast;
    private readonly ISseEventReplayService _replay;

    public OrderEventService(
        ILogger<OrderEventService> logger,
        ISseActivityLog activityLog,
        ISseClientManager clientManager,
        ISseBroadcastService broadcast,
        ISseEventReplayService replay)
    {
        _logger = logger;
        _activityLog = activityLog;
        _clientManager = clientManager;
        _broadcast = broadcast;
        _replay = replay;
    }

    public bool TryAddClient(string clientId, SseClient client) => _clientManager.TryAddClient(clientId, client);

    public void AddClient(string clientId, SseClient client) => _clientManager.AddClient(clientId, client);

    public void RemoveClient(string clientId) => _clientManager.RemoveClient(clientId);

    public object GetClientStatistics() => _clientManager.GetClientStatistics();

    /// <summary>Replays recent events to a newly connected client (delegated to the replay service).</summary>
    public Task ReplayRecentEventsAsync(SseClient client) => _replay.ReplayRecentEventsAsync(client);

    public async Task NotifyStockUpdate(string stock)
    {
        var eventData = new StockEvent
        {
            EventType = "stock-updated",
            PreviousStatus = stock,
            Timestamp = DateTime.UtcNow
        };
        // Notify all staff about the updated order
        await _broadcast.SendEventToClients(eventData, ClientType.All, eventData.EventType);
    }

    public async Task NotifyOrderCreated(OrderDto order)
    {
        var eventData = new OrderEvent
        {
            EventType = "order-created",
            Order = order,
            Timestamp = DateTime.UtcNow
        };

        var clients = _clientManager.GetClients();
        var kitchenClients = clients.Count(c => c.ClientType == ClientType.Kitchen);
        var serviceClients = clients.Count(c => c.ClientType == ClientType.Service);
        var managerClients = clients.Count(c => c.ClientType == ClientType.Manager);
        var allClients = clients.Count;

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
        await _broadcast.SendEventToClients(eventData, ClientType.Kitchen, eventData.EventType);

        // Also notify service staff (cashiers) of new orders
        await _broadcast.SendEventToClients(eventData, ClientType.Service, eventData.EventType);

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
            await _broadcast.SendEventToClients(eventData, clientType, eventData.EventType);
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
        await _broadcast.SendEventToClients(eventData, ClientType.Service, eventData.EventType);

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
        await _broadcast.SendEventToClients(eventData, ClientType.All, eventData.EventType);

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
        await _broadcast.SendEventToClients(eventData, ClientType.All, eventData.EventType);

        _logger.LogInformation("Notified staff about focus order update for {OrderNumber}", order.OrderNumber);
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
}
