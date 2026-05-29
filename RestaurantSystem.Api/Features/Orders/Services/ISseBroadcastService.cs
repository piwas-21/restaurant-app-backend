using RestaurantSystem.Api.Features.Orders.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Serialises an event to the SSE wire frame and fans it out to the matching connected
/// clients (and into the replay buffer). Extracted from OrderEventService (Sprint 3 SSE
/// decomposition, task 3.9), merging the two previously duplicated
/// <c>SendEventToClients</c> overloads into one generic method. Singleton.
/// </summary>
public interface ISseBroadcastService
{
    /// <summary>
    /// Broadcasts <paramref name="eventData"/> (serialised camelCase) to clients of
    /// <paramref name="targetClientType"/> — plus Manager clients, who see everything — and
    /// stores the frame for replay. <paramref name="eventType"/> is the SSE <c>event:</c> name.
    /// </summary>
    Task SendEventToClients<TEvent>(TEvent eventData, ClientType targetClientType, string eventType);
}
