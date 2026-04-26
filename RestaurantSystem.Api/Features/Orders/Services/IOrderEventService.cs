using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderEventService
{
    void AddClient(string clientId, OrderEventService.SseClient client);
    void RemoveClient(string clientId);

    Task NotifyOrderCreated(OrderDto order);
    Task NotifyOrderStatusChanged(OrderDto order, string previousStatus);
    Task NotifyOrderReady(OrderDto order);
    Task NotifyOrderCompleted(OrderDto order);
    Task NotifyFocusOrderUpdate(OrderDto order);

    Task NotifyStockUpdate(string stock);

    object GetClientStatistics();

    /// <summary>
    /// Replays recent events (within 60 seconds) to a newly connected client
    /// </summary>
    Task ReplayRecentEventsAsync(OrderEventService.SseClient client);
}
